using AndreGoepel.FinanceApp.Domain.NetWorth;

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

/// <summary>Current total, the trailing monthly series, and how many accounts lack a balance.</summary>
public sealed record NetWorthOverview(
    decimal Current,
    IReadOnlyList<NetWorthPoint> Series,
    int AccountsWithoutBalance
);
