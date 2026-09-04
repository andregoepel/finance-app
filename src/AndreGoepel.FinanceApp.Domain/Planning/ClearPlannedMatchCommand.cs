using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Removes one transaction's link to a planned occurrence, freeing that
/// transaction — the occurrence's other links (if any) and the transaction's
/// other links (if any) are untouched.
/// </summary>
public sealed record ClearPlannedMatchCommand(
    Guid PlannedItemId,
    DateOnly DueDate,
    Guid TransactionId
);

public static class ClearPlannedMatchCommandHandler
{
    public static async Task<Result> Handle(
        ClearPlannedMatchCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var key = PlannedMatch.KeyFor(
            command.PlannedItemId,
            command.DueDate,
            command.TransactionId
        );
        var match = await session.LoadAsync<PlannedMatch>(key, cancellationToken);
        if (match is null)
        {
            return Result.Ok();
        }

        session.Delete<PlannedMatch>(key);

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (
            stream.Aggregate is not null
            && stream.Aggregate.PlannedLinks.Any(l =>
                l.PlannedItemId == command.PlannedItemId && l.DueDate == command.DueDate
            )
        )
        {
            stream.AppendOne(
                new TransactionPlannedMatchCleared(command.PlannedItemId, command.DueDate)
            );
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
