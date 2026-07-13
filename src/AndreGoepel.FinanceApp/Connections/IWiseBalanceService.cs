using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>
/// Reads a Wise connection's balances (token-only): refreshes the current amount
/// on each linked account (matched by external id = Wise balance id, for net
/// worth) and can create household accounts for balances that are not linked
/// yet — standard currency balances and savings jars alike. Transactions sync
/// separately through the Wise activity connector.
/// </summary>
public interface IWiseBalanceService
{
    Task<Result<WiseBalanceSyncResult>> SyncConnectionAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates an API-synced account for every Wise balance (standard + jars) that
    /// no account is linked to yet, owned by the connection's owner, then refreshes
    /// all balances. Idempotent: already-linked balances are left untouched.
    /// </summary>
    Task<Result<WiseAccountImportResult>> ImportAccountsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Balances fetched from Wise plus how many household accounts were updated.</summary>
public sealed record WiseBalanceSyncResult(
    IReadOnlyList<WiseBalance> Balances,
    int AccountsUpdated
);

/// <summary>Outcome of an account import: what was created and what already existed.</summary>
public sealed record WiseAccountImportResult(
    IReadOnlyList<string> CreatedAccountNames,
    int AlreadyLinked
);
