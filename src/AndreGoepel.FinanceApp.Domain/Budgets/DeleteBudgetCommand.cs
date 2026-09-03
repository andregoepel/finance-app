using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>Removes a budget period.</summary>
public sealed record DeleteBudgetCommand(Guid Id);

public static class DeleteBudgetCommandHandler
{
    public static async Task<Result> Handle(
        DeleteBudgetCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        session.Delete<Budget>(command.Id);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
