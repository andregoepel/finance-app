using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Everything a hard delete of one account takes with it. Used twice: as the
/// preview the confirmation dialog spells out before the user commits, and as
/// the report of what <see cref="HardDeleteAccountCommand"/> actually removed.
/// </summary>
/// <param name="Transactions">Transaction event streams (and their projections) that are deleted.</param>
/// <param name="ImportBatches">Import audit records of this account that are deleted.</param>
/// <param name="TransfersUnlinked">Transactions on <em>other</em> accounts whose transfer link is cleared — those are kept, only unlinked.</param>
/// <param name="PlannedMatchesCleared">Plan-vs-actual matches pointing at deleted transactions.</param>
/// <param name="ReviewQueueEntries">Pending AI suggestions for deleted transactions.</param>
/// <param name="CryptoHoldings">Manually maintained crypto positions of this account.</param>
/// <param name="PlannedItemsDetached">Planned items that expected this account — kept, with the expectation cleared.</param>
public sealed record AccountDeletionImpact(
    int Transactions,
    int ImportBatches,
    int TransfersUnlinked,
    int PlannedMatchesCleared,
    int ReviewQueueEntries,
    int CryptoHoldings,
    int PlannedItemsDetached
)
{
    public static readonly AccountDeletionImpact Nothing = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>True when only the account document itself would go away.</summary>
    public bool IsAccountOnly =>
        Transactions == 0
        && ImportBatches == 0
        && TransfersUnlinked == 0
        && PlannedMatchesCleared == 0
        && ReviewQueueEntries == 0
        && CryptoHoldings == 0
        && PlannedItemsDetached == 0;

    // The prose form of this ("Deletes the account plus 3 transactions and …") deliberately does
    // NOT live here. It needs singular/plural selection and list-joining, which are language
    // rules, not domain rules — German additionally needs case agreement the English version has
    // no notion of. The sentence is assembled in the UI, where the localizer lives, from the
    // counts above: see AccountDeletionDescription in the web project.
}

/// <summary>
/// Reads what <see cref="HardDeleteAccountCommand"/> would remove, without
/// removing anything — the confirmation dialog states the blast radius before
/// the user commits. A plain read against <see cref="IQuerySession"/> like every
/// other query in the UI, running the very collection logic the delete uses so
/// the preview and the deletion can never describe different things.
/// </summary>
public static class AccountDeletionPreview
{
    public static async Task<Result<AccountDeletionImpact>> ForAccountAsync(
        IQuerySession session,
        Guid accountId,
        CancellationToken cancellationToken = default
    )
    {
        var account = await session.LoadAsync<Account>(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<AccountDeletionImpact>("Account not found.");
        }

        var targets = await AccountPurgeTargets.CollectAsync(session, accountId, cancellationToken);
        return Result.Ok(targets.ToImpact());
    }
}

/// <summary>
/// The concrete ids a hard delete touches, gathered in one pass so the preview
/// and the delete itself can never disagree about the blast radius.
/// </summary>
internal sealed record AccountPurgeTargets(
    IReadOnlyList<Guid> TransactionIds,
    IReadOnlyList<Guid> TransferCounterpartIds,
    IReadOnlyList<string> PlannedMatchIds,
    IReadOnlyList<Guid> ReviewQueueIds,
    IReadOnlyList<Guid> ImportBatchIds,
    IReadOnlyList<string> CryptoHoldingIds,
    IReadOnlyList<Guid> PlannedItemIdsToDetach
)
{
    public AccountDeletionImpact ToImpact() =>
        new(
            TransactionIds.Count,
            ImportBatchIds.Count,
            TransferCounterpartIds.Count,
            PlannedMatchIds.Count,
            ReviewQueueIds.Count,
            CryptoHoldingIds.Count,
            PlannedItemIdsToDetach.Count
        );

    public static async Task<AccountPurgeTargets> CollectAsync(
        IQuerySession session,
        Guid accountId,
        CancellationToken cancellationToken
    )
    {
        var transactionIds = (
            await session
                .Query<TransactionView>()
                .Where(t => t.AccountId == accountId)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)
        ).ToArray();

        var importBatchIds = await session
            .Query<ImportBatch>()
            .Where(b => b.AccountId == accountId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var cryptoHoldingIds = await session
            .Query<CryptoHolding>()
            .Where(h => h.AccountId == accountId)
            .Select(h => h.Id)
            .ToListAsync(cancellationToken);

        var plannedItemIds = await session
            .Query<PlannedItem>()
            .Where(p => p.ExpectedAccountId == accountId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (transactionIds.Length == 0)
        {
            return new AccountPurgeTargets(
                [],
                [],
                [],
                [],
                [.. importBatchIds],
                [.. cryptoHoldingIds],
                [.. plannedItemIds]
            );
        }

        var counterpartIds = await CollectTransferCounterpartsAsync(
            session,
            accountId,
            transactionIds,
            cancellationToken
        );

        var plannedMatchIds = await session
            .Query<PlannedMatch>()
            .Where(m => m.TransactionId.IsOneOf(transactionIds))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var reviewQueueIds = await session
            .Query<CategorySuggestion>()
            .Where(s => s.Id.IsOneOf(transactionIds))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        return new AccountPurgeTargets(
            transactionIds,
            counterpartIds,
            [.. plannedMatchIds],
            [.. reviewQueueIds],
            [.. importBatchIds],
            [.. cryptoHoldingIds],
            [.. plannedItemIds]
        );
    }

    /// <summary>
    /// The still-linked transfer legs on other accounts. Legs that are themselves
    /// being deleted are skipped — both sides disappear, so nothing dangles.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> CollectTransferCounterpartsAsync(
        IQuerySession session,
        Guid accountId,
        Guid[] transactionIds,
        CancellationToken cancellationToken
    )
    {
        var linked = await session
            .Query<TransactionView>()
            .Where(t => t.AccountId == accountId && t.TransferCounterpartId != null)
            .ToListAsync(cancellationToken);

        var deleted = transactionIds.ToHashSet();
        var candidates = linked
            .Select(t => t.TransferCounterpartId!.Value)
            .Where(id => !deleted.Contains(id))
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        var stillLinked = await session
            .Query<TransactionView>()
            .Where(t => t.Id.IsOneOf(candidates) && t.TransferCounterpartId != null)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return [.. stillLinked];
    }
}
