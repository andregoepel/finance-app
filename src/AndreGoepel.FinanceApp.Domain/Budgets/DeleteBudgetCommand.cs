using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>Removes the budget for a category.</summary>
public sealed record DeleteBudgetCommand(Guid CategoryId);

public static class DeleteBudgetCommandHandler
{
    public static async Task<Result> Handle(
        DeleteBudgetCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        session.Delete<Budget>(command.CategoryId);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
