using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Links two transactions as the legs of a transfer between own accounts
/// (e.g. Wise → DKB). Linked transactions are excluded from spending
/// aggregations.
/// </summary>
public sealed record LinkTransactionsAsTransferCommand(
    Guid FirstTransactionId,
    Guid SecondTransactionId
);

public static class LinkTransactionsAsTransferCommandHandler
{
    public static async Task<Result> Handle(
        LinkTransactionsAsTransferCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        if (command.FirstTransactionId == command.SecondTransactionId)
        {
            return Result.Fail("A transaction cannot be linked to itself.");
        }

        var first = await session.Events.FetchForWriting<TransactionView>(
            command.FirstTransactionId,
            cancellationToken
        );
        var second = await session.Events.FetchForWriting<TransactionView>(
            command.SecondTransactionId,
            cancellationToken
        );
        if (first.Aggregate is null || second.Aggregate is null)
        {
            return Result.Fail("Transaction not found.");
        }

        if (first.Aggregate.IsTransfer || second.Aggregate.IsTransfer)
        {
            return Result.Fail("One of the transactions is already linked as a transfer.");
        }

        if (first.Aggregate.AccountId == second.Aggregate.AccountId)
        {
            return Result.Fail("Transfer legs must belong to different accounts.");
        }

        first.AppendOne(new TransactionLinkedAsTransfer(command.SecondTransactionId));
        second.AppendOne(new TransactionLinkedAsTransfer(command.FirstTransactionId));

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
