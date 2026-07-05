using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.NetWorth;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="INetWorthService"/> over accounts + the transaction read
/// model, sampling net worth at each month-end over the window (and today). Uses
/// EUR throughout; transactions are included in full (transfers move real balances
/// and net out across accounts for the total).
/// </summary>
internal sealed class NetWorthService(IQuerySession session) : INetWorthService
{
    public async Task<NetWorthOverview> GetAsync(
        int months = 12,
        CancellationToken cancellationToken = default
    )
    {
        var accounts = await session
            .Query<Account>()
            .Where(a => a.Status == AccountStatus.Active)
            .ToListAsync(cancellationToken);

        var anchored = accounts
            .Where(a => a.CurrentBalanceEur is not null && a.BalanceUpdatedAt is not null)
            .ToList();
        var withoutBalance = accounts.Count - anchored.Count;

        var anchors = new List<AccountAnchor>(anchored.Count);
        foreach (var account in anchored)
        {
            var transactions = (
                await session
                    .Query<TransactionView>()
                    .Where(t => t.AccountId == account.Id && t.AmountEur != null)
                    .ToListAsync(cancellationToken)
            )
                .Select(t => (t.BookingDate, t.AmountEur!.Value))
                .ToList();

            anchors.Add(
                new AccountAnchor(
                    account.CurrentBalanceEur!.Value,
                    DateOnly.FromDateTime(account.BalanceUpdatedAt!.Value.UtcDateTime),
                    transactions
                )
            );
        }

        var series = NetWorthCalculator.Compute(anchors, BuildSampleDates(months));
        var current = series.Count > 0 ? series[^1].Amount : 0m;
        return new NetWorthOverview(current, series, withoutBalance);
    }

    /// <summary>Month-end dates for the trailing window, ending with today.</summary>
    private static IReadOnlyList<DateOnly> BuildSampleDates(int months)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dates = new List<DateOnly>();
        for (var i = months - 1; i >= 1; i--)
        {
            var month = today.AddMonths(-i);
            dates.Add(
                new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month))
            );
        }
        dates.Add(today);
        return dates;
    }
}
