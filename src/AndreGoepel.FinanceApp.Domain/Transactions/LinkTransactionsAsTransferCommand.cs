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

        var result = await TransferLinking.LinkAsync(
            session,
            command.FirstTransactionId,
            command.SecondTransactionId,
            localizer,
            cancellationToken
        );
        if (result.IsFailure)
        {
            return result;
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>
/// The validation and event-appending shared by every path that links two
/// transactions as a transfer — a direct manual pick
/// (<see cref="LinkTransactionsAsTransferCommandHandler"/>) and accepting a
/// suggestion (<see cref="AcceptTransferSuggestionCommandHandler"/>). Neither
/// caller commits here — that stays with the caller so it can save other
/// changes (like clearing competing suggestions) in the same unit of work.
/// </summary>
internal static class TransferLinking
{
    public static async Task<Result> LinkAsync(
        IDocumentSession session,
        Guid firstTransactionId,
        Guid secondTransactionId,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var first = await session.Events.FetchForWriting<TransactionView>(
            firstTransactionId,
            cancellationToken
        );
        var second = await session.Events.FetchForWriting<TransactionView>(
            secondTransactionId,
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

        first.AppendOne(new TransactionLinkedAsTransfer(secondTransactionId));
        second.AppendOne(new TransactionLinkedAsTransfer(firstTransactionId));
        return Result.Ok();
    }
}
