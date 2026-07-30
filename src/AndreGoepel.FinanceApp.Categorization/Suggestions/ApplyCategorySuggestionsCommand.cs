using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Categorization.Suggestions;

/// <summary>
/// Bulk-confirms AI suggestions from the review queue. Confirmation is a human
/// decision, so the resulting event carries source Manual — these transactions
/// also feed the few-shot examples for future AI batches.
/// </summary>
public sealed record ApplyCategorySuggestionsCommand(IReadOnlyList<Guid> TransactionIds);

public static class ApplyCategorySuggestionsCommandHandler
{
    public static async Task<Result> Handle(
        ApplyCategorySuggestionsCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var applied = 0;
        foreach (var transactionId in command.TransactionIds.Distinct())
        {
            var suggestion = await session.LoadAsync<CategorySuggestion>(
                transactionId,
                cancellationToken
            );
            if (suggestion is null)
            {
                continue;
            }

            var stream = await session.Events.FetchForWriting<TransactionView>(
                transactionId,
                cancellationToken
            );
            if (stream.Aggregate is { CategoryId: null })
            {
                stream.AppendOne(
                    new TransactionCategorized(suggestion.CategoryId, CategorySource.Manual, null)
                );
                applied++;
            }
            session.Delete(suggestion);
        }

        await session.SaveChangesAsync(cancellationToken);
        return applied == 0 && command.TransactionIds.Count > 0
            ? Result.Fail("No pending suggestions found for the selected transactions.")
            : Result.Ok();
    }
}
