using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Takes back a mistyped manual entry: the transaction stream, its one-row import
/// batch and whatever hangs off it (transfer link, plan match, review-queue entry)
/// go away and the account's ledger balance moves back. Only entries on manually
/// maintained accounts qualify — imported history is never deleted one row at a
/// time; clearing or hard-deleting the account is the path for that.
/// </summary>
public sealed record DeleteManualTransactionCommand(Guid TransactionId);

public static class DeleteManualTransactionCommandHandler
{
    public static async Task<Result> Handle(
        DeleteManualTransactionCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var transaction = await session.LoadAsync<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (transaction is null)
        {
            return Result.Fail(localizer["Error.TransactionNotFound"]);
        }

        var account = await session.LoadAsync<Account>(transaction.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail(localizer["Error.AccountNotFound"]);
        }
        if (account.SyncMethod != SyncMethod.Manual)
        {
            return Result.Fail(localizer["Error.OnlyManualEntriesCanBeDeleted"]);
        }

        var targets = await AccountPurgeTargets.CollectForTransactionAsync(
            session,
            transaction,
            cancellationToken
        );
        await AccountTransactionPurge.ApplyAsync(session, targets, cancellationToken);

        ManualAccountLedger.Move(
            account,
            -transaction.Amount,
            -transaction.AmountEur,
            DateTimeOffset.UtcNow
        );
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
