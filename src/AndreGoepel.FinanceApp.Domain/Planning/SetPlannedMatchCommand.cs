using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Manually links a transaction to a planned occurrence. Additive: an occurrence
/// may already have other transactions linked (e.g. a salary paid out in two
/// bookings) and the transaction may already be linked to other occurrences
/// (e.g. one transfer covering rent and a car payment) — neither is disturbed.
/// Re-linking the same pairing is a no-op.
/// </summary>
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
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var item = await session.LoadAsync<PlannedItem>(command.PlannedItemId, cancellationToken);
        if (item is null)
        {
            return Result.Fail(localizer["Error.PlannedItemNotFound"]);
        }

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail(localizer["Error.TransactionNotFound"]);
        }

        session.Store(
            new PlannedMatch
            {
                Id = PlannedMatch.KeyFor(
                    command.PlannedItemId,
                    command.DueDate,
                    command.TransactionId
                ),
                PlannedItemId = command.PlannedItemId,
                DueDate = command.DueDate,
                TransactionId = command.TransactionId,
                Auto = false,
            }
        );

        if (
            !stream.Aggregate.PlannedLinks.Any(l =>
                l.PlannedItemId == command.PlannedItemId && l.DueDate == command.DueDate
            )
        )
        {
            stream.AppendOne(
                new TransactionMatchedToPlannedItem(command.PlannedItemId, command.DueDate)
            );
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
