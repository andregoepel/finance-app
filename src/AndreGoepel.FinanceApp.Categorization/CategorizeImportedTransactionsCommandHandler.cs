using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Categorization.History;
using AndreGoepel.FinanceApp.Categorization.Rules;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace AndreGoepel.FinanceApp.Categorization;

/// <summary>
/// Async categorization in three stages, cheapest first: learned rules, then
/// the household's own history (a counterparty confirmed by hand twice with
/// the same category), then Claude for whatever is left, in batches of 50 and
/// with examples and recurrence notes drawn from that same history. High
/// confidence auto-applies (flagged "AI" in the grid); low confidence becomes a
/// stored suggestion for the review queue. Any Claude failure leaves the
/// remaining transactions uncategorized — they surface in the review queue and
/// the next run retries; the import itself has long since succeeded.
/// </summary>
/// <remarks>
/// Two entry points share the pipeline: the per-import follow-up published
/// after every upload or sync, and the backfill over everything still
/// uncategorized, triggered from the review page.
/// <para>
/// Must stay <c>public</c>: Wolverine's handler discovery skips non-public
/// types (the generated handler code has to reference the class), and the
/// <see cref="WolverineHandlerAttribute"/> does not override that. An
/// <c>internal</c> handler leaves the command without a subscriber and it
/// is dropped silently. Guarded by <c>HandlerDiscoveryTests</c>.
/// </para>
/// </remarks>
[WolverineHandler]
public sealed class CategorizeImportedTransactionsCommandHandler
{
    internal const decimal HighConfidenceThreshold = 0.8m;
    private const int BatchSize = 50;
    private const int FewShotExampleCount = 30;

    /// <summary>Import follow-up: the batch's rows that are still uncategorized.</summary>
    public async Task Handle(
        CategorizeImportedTransactionsCommand command,
        IDocumentSession session,
        IClaudeCategorizer claudeCategorizer,
        ILogger<CategorizeImportedTransactionsCommandHandler> logger,
        CancellationToken cancellationToken
    )
    {
        var pending = await session
            .Query<TransactionView>()
            .Where(t => t.ImportBatchId == command.ImportBatchId && t.CategoryId == null)
            .ToListAsync(cancellationToken);

        await CategorizeAsync(
            pending.ToList(),
            $"import batch {command.ImportBatchId}",
            session,
            claudeCategorizer,
            logger,
            cancellationToken
        );
    }

    /// <summary>
    /// Backfill: every uncategorized transaction across all imports, minus
    /// transfer legs (never categorized) and transactions that already carry a
    /// pending suggestion (the reviewer has not decided yet; asking again would
    /// only burn tokens for the same answer).
    /// </summary>
    public async Task Handle(
        CategorizeUncategorizedTransactionsCommand command,
        IDocumentSession session,
        IClaudeCategorizer claudeCategorizer,
        ILogger<CategorizeImportedTransactionsCommandHandler> logger,
        CancellationToken cancellationToken
    )
    {
        var uncategorized = await session
            .Query<TransactionView>()
            .Where(t => t.CategoryId == null && t.TransferCounterpartId == null)
            .ToListAsync(cancellationToken);

        var awaitingReview = (
            await session
                .Query<CategorySuggestion>()
                .Select(s => s.Id)
                .ToListAsync(cancellationToken)
        ).ToHashSet();
        var pending = uncategorized.Where(t => !awaitingReview.Contains(t.Id)).ToList();

        logger.LogInformation(
            "Backfill categorization: {Pending} of {Uncategorized} uncategorized transactions eligible.",
            pending.Count,
            uncategorized.Count
        );

        await CategorizeAsync(
            pending,
            "backfill",
            session,
            claudeCategorizer,
            logger,
            cancellationToken
        );
    }

    private static async Task CategorizeAsync(
        List<TransactionView> pending,
        string scope,
        IDocumentSession session,
        IClaudeCategorizer claudeCategorizer,
        ILogger<CategorizeImportedTransactionsCommandHandler> logger,
        CancellationToken cancellationToken
    )
    {
        if (pending.Count == 0)
        {
            return;
        }

        // Stage 1: learned rules.
        var rules = await session.Query<CategoryRule>().ToListAsync(cancellationToken);
        var unmatched = new List<TransactionView>();
        foreach (var transaction in pending)
        {
            var rule = RuleMatcher.FindMatch(
                rules.ToList(),
                transaction.Counterparty,
                transaction.Description,
                transaction.Amount
            );
            if (rule is null)
            {
                unmatched.Add(transaction);
                continue;
            }

            await AppendCategorizedAsync(
                session,
                transaction.Id,
                new TransactionCategorized(rule.CategoryId, CategorySource.Rule, null),
                cancellationToken
            );
        }
        await session.SaveChangesAsync(cancellationToken);

        if (unmatched.Count == 0)
        {
            return;
        }

        // Stage 2: the household's own confirmed history.
        var categories = await session.Query<Category>().ToListAsync(cancellationToken);
        var options = CategoryPaths.Build(categories.ToList());
        var history = new CategorizationHistory(
            await LoadHistoryAsync(session, cancellationToken),
            options
        );

        var forClaude = new List<TransactionView>();
        foreach (var transaction in unmatched)
        {
            if (history.ConsistentCategoryFor(transaction.Counterparty) is not Guid categoryId)
            {
                forClaude.Add(transaction);
                continue;
            }

            await AppendCategorizedAsync(
                session,
                transaction.Id,
                new TransactionCategorized(categoryId, CategorySource.History, null),
                cancellationToken
            );
        }
        await session.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Categorization {Scope}: {Rules} by rule, {History} by history, {Claude} for Claude.",
            scope,
            pending.Count - unmatched.Count,
            unmatched.Count - forClaude.Count,
            forClaude.Count
        );

        if (forClaude.Count == 0)
        {
            return;
        }

        // Stage 3: Claude, with examples and recurrence notes from the same history.
        foreach (var batch in forClaude.Chunk(BatchSize))
        {
            var toCategorize = batch
                .Select(t => new TransactionToCategorize(
                    t.Id,
                    t.Counterparty,
                    t.Description,
                    t.Amount,
                    t.Currency,
                    t.BookingDate,
                    history.RecurrenceHintFor(t.Counterparty)
                ))
                .ToList();
            var examples = history.ExamplesFor(
                batch.Select(t => t.Counterparty),
                FewShotExampleCount
            );

            var result = await claudeCategorizer.SuggestAsync(
                toCategorize,
                options,
                examples,
                cancellationToken
            );
            if (result.IsFailure)
            {
                // Graceful degradation: stay uncategorized → review queue.
                logger.LogWarning(
                    "AI categorization skipped for {Scope}: {Reason}",
                    scope,
                    result.Error
                );
                return;
            }

            foreach (var suggestion in result.Value!)
            {
                if (suggestion.CategoryId is not Guid categoryId)
                {
                    continue; // model declined — stays in the review queue
                }

                if (suggestion.Confidence >= HighConfidenceThreshold)
                {
                    await AppendCategorizedAsync(
                        session,
                        suggestion.TransactionId,
                        new TransactionCategorized(
                            categoryId,
                            CategorySource.Ai,
                            suggestion.Confidence
                        ),
                        cancellationToken
                    );
                }
                else
                {
                    session.Store(
                        new CategorySuggestion
                        {
                            Id = suggestion.TransactionId,
                            CategoryId = categoryId,
                            Confidence = suggestion.Confidence,
                        }
                    );
                }
            }
            await session.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Appends the categorization unless the stream was categorized in the
    /// meantime (a concurrent run, a manual pick while the batch was in flight).
    /// </summary>
    private static async Task AppendCategorizedAsync(
        IDocumentSession session,
        Guid transactionId,
        TransactionCategorized categorized,
        CancellationToken cancellationToken
    )
    {
        var stream = await session.Events.FetchForWriting<TransactionView>(
            transactionId,
            cancellationToken
        );
        if (stream.Aggregate is { CategoryId: null })
        {
            stream.AppendOne(categorized);
        }
    }

    /// <summary>
    /// Every non-transfer transaction of the household, reduced to the fields
    /// the history needs. Includes the rows being categorized right now — they
    /// are occurrences of their counterparty too, which is what recurrence
    /// detection counts.
    /// </summary>
    private static async Task<IReadOnlyList<HistoryEntry>> LoadHistoryAsync(
        IDocumentSession session,
        CancellationToken cancellationToken
    ) =>
        await session
            .Query<TransactionView>()
            .Where(t => t.TransferCounterpartId == null)
            .Select(t => new HistoryEntry
            {
                Id = t.Id,
                Counterparty = t.Counterparty,
                Description = t.Description,
                Amount = t.Amount,
                AmountEur = t.AmountEur,
                BookingDate = t.BookingDate,
                CategoryId = t.CategoryId,
                CategorySource = t.CategorySource,
            })
            .ToListAsync(cancellationToken);
}
