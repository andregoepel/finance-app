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
    Task<AccountSyncSummary> SyncAccountAsync(
        Guid accountId,
        string? triggeredBy,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<AccountSyncSummary>> SyncAllAsync(
        string? triggeredBy,
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
