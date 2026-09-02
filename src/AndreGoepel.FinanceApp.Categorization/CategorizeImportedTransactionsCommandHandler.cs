using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Categorization.Rules;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace AndreGoepel.FinanceApp.Categorization;

/// <summary>
/// Async categorization: learned rules first (free, deterministic), then Claude
/// for the remainder in batches of 50. High confidence auto-applies (flagged
/// "AI" in the grid); low confidence becomes a stored suggestion for the review
/// queue. Any Claude failure leaves the remaining transactions uncategorized —
/// they surface in the review queue and the next run retries; the import itself
/// has long since succeeded.
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

            var stream = await session.Events.FetchForWriting<TransactionView>(
                transaction.Id,
                cancellationToken
            );
            if (stream.Aggregate is { CategoryId: null })
            {
                stream.AppendOne(
                    new TransactionCategorized(rule.CategoryId, CategorySource.Rule, null)
                );
            }
        }
        await session.SaveChangesAsync(cancellationToken);

        if (unmatched.Count == 0)
        {
            return;
        }

        var categories = await session.Query<Category>().ToListAsync(cancellationToken);
        var options = CategoryPaths.Build(categories.ToList());
        var examples = await LoadFewShotExamplesAsync(session, categories, cancellationToken);

        foreach (var batch in unmatched.Chunk(BatchSize))
        {
            var toCategorize = batch
                .Select(t => new TransactionToCategorize(
                    t.Id,
                    t.Counterparty,
                    t.Description,
                    t.Amount,
                    t.Currency
                ))
                .ToList();

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
                    var stream = await session.Events.FetchForWriting<TransactionView>(
                        suggestion.TransactionId,
                        cancellationToken
                    );
                    if (stream.Aggregate is { CategoryId: null })
                    {
                        stream.AppendOne(
                            new TransactionCategorized(
                                categoryId,
                                CategorySource.Ai,
                                suggestion.Confidence
                            )
                        );
                    }
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

    private static async Task<List<FewShotExample>> LoadFewShotExamplesAsync(
        IDocumentSession session,
        IReadOnlyList<Category> categories,
        CancellationToken cancellationToken
    )
    {
        var byId = categories.ToDictionary(category => category.Id);
        var confirmed = await session
            .Query<TransactionView>()
            .Where(t => t.CategorySource == CategorySource.Manual && t.CategoryId != null)
            .OrderByDescending(t => t.BookingDate)
            .Take(FewShotExampleCount)
            .ToListAsync(cancellationToken);

        return confirmed
            .Where(t => byId.ContainsKey(t.CategoryId!.Value))
            .Select(t => new FewShotExample(
                t.Counterparty,
                t.Description,
                t.Amount,
                CategoryPaths.PathOf(byId[t.CategoryId!.Value], byId)
            ))
            .ToList();
    }
}
