using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.NetWorth;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Reconstructs the household net-worth series (EUR) from each account's balance
/// anchor and its transactions. Accounts without a balance anchor cannot be
/// placed and are excluded (counted separately).
/// </summary>
public interface INetWorthService
{
    Task<NetWorthOverview> GetAsync(int months = 12, CancellationToken cancellationToken = default);
}

/// <summary>
/// Current total, the trailing monthly series, how many accounts lack a balance,
/// and every active account's own current balance. The account balances and
/// <see cref="Current"/> come out of one computation, so the per-account figures
/// always add up to the total.
/// </summary>
public sealed record NetWorthOverview(
    decimal Current,
    IReadOnlyList<NetWorthPoint> Series,
    int AccountsWithoutBalance,
    IReadOnlyList<AccountBalance> Accounts,
    IReadOnlyList<NetWorthPoint>? Forecast = null
);

/// <summary>
/// One active account's current balance for the dashboard. <see cref="BalanceEur"/>
/// is the anchor projected to today by the EUR transactions after the anchor date
/// (the same figure the net-worth total sums); <see cref="Balance"/> is the same
/// projection in the account's own currency, using only transactions booked in that
/// currency. Both are <c>null</c> when the account has no balance anchor yet.
/// </summary>
public sealed record AccountBalance(
    Guid AccountId,
    string Name,
    ProviderKind Provider,
    AccountType Type,
    string Currency,
    decimal? Balance,
    decimal? BalanceEur,
    DateTimeOffset? AsOf
)
{
    public bool HasBalance => BalanceEur is not null;

    /// <summary>True when the native figure adds information beyond the EUR one.</summary>
    public bool IsForeignCurrency =>
        !string.IsNullOrWhiteSpace(Currency)
        && !Currency.Equals("EUR", StringComparison.OrdinalIgnoreCase);
}
