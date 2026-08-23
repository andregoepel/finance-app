using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

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
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (command.FirstTransactionId == command.SecondTransactionId)
        {
            return Result.Fail(localizer["Error.CannotLinkToItself"]);
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
            return Result.Fail(localizer["Error.TransactionNotFound"]);
        }

        if (first.Aggregate.IsTransfer || second.Aggregate.IsTransfer)
        {
            return Result.Fail(localizer["Error.AlreadyLinkedAsTransfer"]);
        }

        if (first.Aggregate.AccountId == second.Aggregate.AccountId)
        {
            return Result.Fail(localizer["Error.TransferLegsSameAccount"]);
        }

        first.AppendOne(new TransactionLinkedAsTransfer(command.SecondTransactionId));
        second.AppendOne(new TransactionLinkedAsTransfer(command.FirstTransactionId));

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
