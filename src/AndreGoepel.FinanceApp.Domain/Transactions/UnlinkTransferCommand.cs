using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>Reverts an erroneous transfer link on both legs.</summary>
public sealed record UnlinkTransferCommand(Guid TransactionId);

public static class UnlinkTransferCommandHandler
{
    public static async Task<Result> Handle(
        UnlinkTransferCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail("Transaction not found.");
        }

        if (stream.Aggregate.TransferCounterpartId is not Guid counterpartId)
        {
            return Result.Fail("Transaction is not linked as a transfer.");
        }

        stream.AppendOne(new TransactionTransferUnlinked());

        var counterpart = await session.Events.FetchForWriting<TransactionView>(
            counterpartId,
            cancellationToken
        );
        if (counterpart.Aggregate is not null && counterpart.Aggregate.IsTransfer)
        {
            counterpart.AppendOne(new TransactionTransferUnlinked());
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
