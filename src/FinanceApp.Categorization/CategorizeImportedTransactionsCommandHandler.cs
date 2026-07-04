using FinanceApp.Categorization.Claude;
using FinanceApp.Categorization.Rules;
using FinanceApp.Categorization.Suggestions;
using FinanceApp.Domain.Categories;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace FinanceApp.Categorization;

/// <summary>
/// Async categorization after an import: learned rules first (free,
/// deterministic), then Claude for the remainder in batches of 50. High
/// confidence auto-applies (flagged "AI" in the grid); low confidence becomes a
/// stored suggestion for the review queue. Any Claude failure leaves the
/// remaining transactions uncategorized — they surface in the review queue and
/// the next import retries; the import itself has long since succeeded.
/// </summary>
[WolverineHandler]
public class CategorizeImportedTransactionsCommandHandler
{
    internal const decimal HighConfidenceThreshold = 0.8m;
    private const int BatchSize = 50;
    private const int FewShotExampleCount = 30;

    public async Task Handle(
        CategorizeImportedTransactionsCommand command,
        IDocumentSession session,
        IClaudeCategorizer claudeCategorizer,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var logger = loggerFactory.CreateLogger("FinanceApp.Categorization");

        var pending = await session
            .Query<TransactionView>()
            .Where(t => t.ImportBatchId == command.ImportBatchId && t.CategoryId == null)
            .ToListAsync(cancellationToken);
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
                    "AI categorization skipped for batch {BatchId}: {Reason}",
                    command.ImportBatchId,
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
