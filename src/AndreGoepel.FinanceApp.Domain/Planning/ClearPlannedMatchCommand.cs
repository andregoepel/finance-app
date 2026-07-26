using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>Removes the match for a planned occurrence, freeing its transaction.</summary>
public sealed record ClearPlannedMatchCommand(Guid PlannedItemId, DateOnly DueDate);

public static class ClearPlannedMatchCommandHandler
{
    public static async Task<Result> Handle(
        ClearPlannedMatchCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var key = PlannedMatch.KeyFor(command.PlannedItemId, command.DueDate);
        var match = await session.LoadAsync<PlannedMatch>(key, cancellationToken);
        if (match is null)
        {
            return Result.Ok();
        }

        session.Delete<PlannedMatch>(key);

        var stream = await session.Events.FetchForWriting<TransactionView>(
            match.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is { PlannedItemId: not null })
        {
            stream.AppendOne(new TransactionPlannedMatchCleared());
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
