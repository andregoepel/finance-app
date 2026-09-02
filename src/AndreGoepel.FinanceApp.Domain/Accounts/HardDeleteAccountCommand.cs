using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Purges an account and everything hanging off it: transaction event streams
/// and their projections, import batches, crypto holdings, plan-vs-actual
/// matches and review-queue entries. Irreversible — the transaction event
/// history goes with it, including the <c>TransactionCategoryCorrected</c>
/// events that fed rule learning. The learned <see cref="CategoryRule"/>s
/// themselves are keyed on text patterns, not on accounts, and survive.
/// <see cref="DeleteAccountCommand"/> stays the safe default: it refuses as soon
/// as the account has any transaction, and
/// <see cref="ClearAccountTransactionsCommand"/> sits in between, clearing the
/// history while keeping the account.
/// </summary>
public sealed record HardDeleteAccountCommand(Guid AccountId);

public static class HardDeleteAccountCommandHandler
{
    /// <summary>
    /// One session, one <c>SaveChangesAsync</c> — the whole cascade commits as a
    /// single Postgres transaction or not at all, so a failure can never leave
    /// transactions without their account or transfer links pointing at ghosts.
    /// </summary>
    public static async Task<Result<AccountDeletionImpact>> Handle(
        HardDeleteAccountCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<AccountDeletionImpact>(localizer["Error.AccountNotFound"]);
        }

        var targets = await AccountPurgeTargets.CollectAsync(
            session,
            command.AccountId,
            cancellationToken
        );

        await AccountTransactionPurge.ApplyAsync(session, targets, cancellationToken);
        await DetachPlannedItemsAsync(session, targets, cancellationToken);

        foreach (var holdingId in targets.CryptoHoldingIds)
        {
            session.Delete<CryptoHolding>(holdingId);
        }

        session.Delete(account);

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(targets.ToImpact());
    }

    private static async Task DetachPlannedItemsAsync(
        IDocumentSession session,
        AccountPurgeTargets targets,
        CancellationToken cancellationToken
    )
    {
        foreach (var plannedItemId in targets.PlannedItemIdsToDetach)
        {
            var item = await session.LoadAsync<PlannedItem>(plannedItemId, cancellationToken);
            if (item is null)
            {
                continue;
            }

            item.ExpectedAccountId = null;
            session.Store(item);
        }
    }
}
