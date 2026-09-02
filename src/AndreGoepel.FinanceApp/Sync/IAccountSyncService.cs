using System.Diagnostics.CodeAnalysis;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Orchestrates an API sync for an account: pick the connector, fetch the window,
/// then hand the rows to the same <c>ImportStatementCommand</c> pipeline a CSV
/// upload uses (dedup, <c>TransactionImported</c> events, <c>ImportBatch</c>
/// audit) and kick off categorization. Runs as application code — the proven
/// top-level Wolverine publish path — not inside a message handler.
/// </summary>
public interface IAccountSyncService
{
    /// <param name="fullHistory">
    /// Ignore the account's last import batch and Wise's default backfill window,
    /// and fetch from <see cref="AccountSyncService.FullHistorySince"/> instead — a
    /// one-off deep sync, e.g. after a truncated first sync or a provider bug that
    /// under-fetched history. Enable Banking accounts are still bound by whatever
    /// the bank's PSD2 consent actually allows.
    /// </param>
    Task<AccountSyncSummary> SyncAccountAsync(
        Guid accountId,
        string? triggeredBy,
        bool fullHistory = false,
        CancellationToken cancellationToken = default
    );

    /// <param name="connectionId">Restrict to accounts synced through this connection; null syncs every API account.</param>
    Task<IReadOnlyList<AccountSyncSummary>> SyncAllAsync(
        string? triggeredBy,
        bool fullHistory = false,
        Guid? connectionId = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Outcome of syncing one account — surfaced in the Sync page and logged by the scheduler.</summary>
public sealed record AccountSyncSummary(
    Guid AccountId,
    string AccountName,
    bool Success,
    string? Error,
    int Imported,
    int Duplicates
)
{
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !Success;
}
