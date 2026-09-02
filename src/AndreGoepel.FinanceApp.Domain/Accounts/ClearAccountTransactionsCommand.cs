using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Empties an account's imported history but keeps the account: transaction event
/// streams and their projections go, along with the import batches that produced
/// them and the plan matches and review-queue entries pointing at them; transfer
/// legs on other accounts are unlinked rather than removed. The account itself, its
/// balance, its crypto holdings and any planned items expecting it all stay — this
/// is "start this account's import over", not a deletion.
/// <para>
/// Dropping the import batches is the point of it for API accounts: the sync window
/// is derived from the newest batch, so clearing them lets the next sync backfill
/// from scratch instead of resuming where the bad data left off.
/// </para>
/// <para>
/// Irreversible in the same way <see cref="HardDeleteAccountCommand"/> is: the
/// <c>TransactionCategoryCorrected</c> events that fed rule learning go with the
/// streams, while the learned <see cref="CategoryRule"/>s themselves — keyed on text
/// patterns, not accounts — survive.
/// </para>
/// </summary>
public sealed record ClearAccountTransactionsCommand(Guid AccountId);

public static class ClearAccountTransactionsCommandHandler
{
    /// <summary>
    /// One session, one <c>SaveChangesAsync</c>, exactly as the hard delete does:
    /// the cascade commits whole or not at all, so a failure cannot strand
    /// transactions whose import batch is already gone.
    /// </summary>
    public static async Task<Result<AccountTransactionsClearedImpact>> Handle(
        ClearAccountTransactionsCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<AccountTransactionsClearedImpact>(
                localizer["Error.AccountNotFound"]
            );
        }

        var targets = await AccountPurgeTargets.CollectAsync(
            session,
            command.AccountId,
            cancellationToken
        );

        await AccountTransactionPurge.ApplyAsync(session, targets, cancellationToken);

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(targets.ToTransactionsImpact());
    }
}

/// <summary>
/// What clearing one account's history removes. Used twice: as the preview the
/// confirmation dialog spells out, and as the report of what
/// <see cref="ClearAccountTransactionsCommand"/> actually removed.
/// </summary>
/// <param name="Transactions">Transaction event streams (and their projections) that are deleted.</param>
/// <param name="ImportBatches">Import audit records of this account that are deleted.</param>
/// <param name="TransfersUnlinked">Transactions on <em>other</em> accounts whose transfer link is cleared — those are kept, only unlinked.</param>
/// <param name="PlannedMatchesCleared">Plan-vs-actual matches pointing at deleted transactions.</param>
/// <param name="ReviewQueueEntries">Pending AI suggestions for deleted transactions.</param>
public sealed record AccountTransactionsClearedImpact(
    int Transactions,
    int ImportBatches,
    int TransfersUnlinked,
    int PlannedMatchesCleared,
    int ReviewQueueEntries
)
{
    public static readonly AccountTransactionsClearedImpact Nothing = new(0, 0, 0, 0, 0);

    /// <summary>True when the account has no history to clear.</summary>
    public bool IsEmpty =>
        Transactions == 0
        && ImportBatches == 0
        && TransfersUnlinked == 0
        && PlannedMatchesCleared == 0
        && ReviewQueueEntries == 0;

    // As with AccountDeletionImpact, the prose form is assembled in the UI where the
    // localizer lives — singular/plural and list-joining are language rules, not
    // domain rules. See AccountDeletionDescription in the web project.
}

/// <summary>
/// Reads what <see cref="ClearAccountTransactionsCommand"/> would remove, without
/// removing anything, so the confirmation dialog can state the blast radius up
/// front. Runs the very collection logic the command uses, so preview and outcome
/// cannot describe different things.
/// </summary>
public static class AccountTransactionsClearPreview
{
    public static async Task<Result<AccountTransactionsClearedImpact>> ForAccountAsync(
        IQuerySession session,
        Guid accountId,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken = default
    )
    {
        var account = await session.LoadAsync<Account>(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<AccountTransactionsClearedImpact>(
                localizer["Error.AccountNotFound"]
            );
        }

        var targets = await AccountPurgeTargets.CollectAsync(session, accountId, cancellationToken);
        return Result.Ok(targets.ToTransactionsImpact());
    }
}
