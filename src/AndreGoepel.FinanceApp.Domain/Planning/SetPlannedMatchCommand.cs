using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>Manually matches a transaction to a planned occurrence (overrides auto-matching).</summary>
public sealed record SetPlannedMatchCommand(
    Guid PlannedItemId,
    DateOnly DueDate,
    Guid TransactionId
);

public static class SetPlannedMatchCommandHandler
{
    public static async Task<Result> Handle(
        SetPlannedMatchCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var item = await session.LoadAsync<PlannedItem>(command.PlannedItemId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Planned item not found.");
        }

        var key = PlannedMatch.KeyFor(command.PlannedItemId, command.DueDate);

        // If this occurrence is already matched to a different transaction, clear that one's link.
        var current = await session.LoadAsync<PlannedMatch>(key, cancellationToken);
        if (current is not null && current.TransactionId != command.TransactionId)
        {
            await ClearTransactionLinkAsync(session, current.TransactionId, cancellationToken);
        }

        // A transaction satisfies at most one occurrence — drop any other match using it.
        var others = await session
            .Query<PlannedMatch>()
            .Where(m => m.TransactionId == command.TransactionId && m.Id != key)
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            session.Delete<PlannedMatch>(other.Id);
        }

        session.Store(
            new PlannedMatch
            {
                Id = key,
                PlannedItemId = command.PlannedItemId,
                DueDate = command.DueDate,
                TransactionId = command.TransactionId,
                Auto = false,
            }
        );

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail("Transaction not found.");
        }
        if (stream.Aggregate.PlannedItemId != command.PlannedItemId)
        {
            stream.AppendOne(
                new TransactionMatchedToPlannedItem(command.PlannedItemId, command.DueDate)
            );
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    private static async Task ClearTransactionLinkAsync(
        IDocumentSession session,
        Guid transactionId,
        CancellationToken cancellationToken
    )
    {
        var stream = await session.Events.FetchForWriting<TransactionView>(
            transactionId,
            cancellationToken
        );
        if (stream.Aggregate is { PlannedItemId: not null })
        {
            stream.AppendOne(new TransactionPlannedMatchCleared());
        }
    }
}
