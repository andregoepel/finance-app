using AndreGoepel.FinanceApp.Domain;
using Marten;

namespace AndreGoepel.FinanceApp.Categorization.Suggestions;

/// <summary>Discards AI suggestions; the transactions stay in the review queue.</summary>
public sealed record DismissCategorySuggestionsCommand(IReadOnlyList<Guid> TransactionIds);

public static class DismissCategorySuggestionsCommandHandler
{
    public static async Task<Result> Handle(
        DismissCategorySuggestionsCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        foreach (var transactionId in command.TransactionIds.Distinct())
        {
            session.Delete<CategorySuggestion>(transactionId);
        }
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
