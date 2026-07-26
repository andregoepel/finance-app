using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>
/// Refreshes Wise account balances for a connection (token-only, net worth). Wise
/// statement/transaction reads need SCA and are out of scope, so this is a balance
/// reader, not a transaction sync: it fetches the profile's balances and writes
/// the current amount onto each linked account (matched by external id = Wise
/// balance id).
/// </summary>
public interface IWiseBalanceService
{
    Task<Result<WiseBalanceSyncResult>> SyncConnectionAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Balances fetched from Wise plus how many household accounts were updated.</summary>
public sealed record WiseBalanceSyncResult(
    IReadOnlyList<WiseBalance> Balances,
    int AccountsUpdated
);
