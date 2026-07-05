using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

public sealed record DeletePlannedItemCommand(Guid Id);

public static class DeletePlannedItemCommandHandler
{
    public static async Task<Result> Handle(
        DeletePlannedItemCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        session.Delete<PlannedItem>(command.Id);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
