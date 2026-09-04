using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.NetWorth;
using AndreGoepel.FinanceApp.Domain.Planning;
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

        var today = DateOnly.FromDateTime(DateTime.Today);
        var anchors = new List<AccountAnchor>();
        var balances = new List<AccountBalance>(accounts.Count);

        foreach (var account in accounts)
        {
            if (account.CurrentBalanceEur is null || account.BalanceUpdatedAt is null)
            {
                balances.Add(ToBalance(account, balance: null, balanceEur: null));
                continue;
            }

            var transactions = await session
                .Query<TransactionView>()
                .Where(t => t.AccountId == account.Id)
                .ToListAsync(cancellationToken);

            var anchorDate = DateOnly.FromDateTime(account.BalanceUpdatedAt.Value.UtcDateTime);
            var anchor = new AccountAnchor(
                account.CurrentBalanceEur.Value,
                anchorDate,
                transactions
                    .Where(t => t.AmountEur is not null)
                    .Select(t => (t.BookingDate, t.AmountEur!.Value))
                    .ToList()
            );
            anchors.Add(anchor);

            // The native balance follows the same projection, restricted to the
            // account's own currency: on a multi-currency account the other
            // currencies are only meaningful in EUR.
            decimal? native = account.CurrentBalance is decimal nativeAnchor
                ? NetWorthCalculator.BalanceAt(
                    nativeAnchor,
                    anchorDate,
                    transactions
                        .Where(t =>
                            string.Equals(
                                t.Currency,
                                account.Currency,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Select(t => (t.BookingDate, t.Amount))
                        .ToList(),
                    today
                )
                : null;

            balances.Add(ToBalance(account, native, NetWorthCalculator.BalanceAt(anchor, today)));
        }

        var series = NetWorthCalculator.Compute(anchors, BuildSampleDates(months, today));
        var current = series.Count > 0 ? series[^1].Amount : 0m;
        var withoutBalance = balances.Count(b => !b.HasBalance);
        var forecast = await BuildForecastAsync(current, today, cancellationToken);

        return new NetWorthOverview(
            current,
            series,
            withoutBalance,
            balances.OrderBy(b => b.Type).ThenBy(b => b.Name).ToList(),
            forecast
        );
    }

    private async Task<IReadOnlyList<NetWorthPoint>> BuildForecastAsync(
        decimal current,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var items = await session
            .Query<PlannedItem>()
            .Where(item => item.Active)
            .ToListAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var matches =
            itemIds.Length == 0
                ? []
                : await session
                    .Query<PlannedMatch>()
                    .Where(match => match.PlannedItemId.IsOneOf(itemIds))
                    .ToListAsync(cancellationToken);
        var matchedOccurrences = matches
            .Select(match => (match.PlannedItemId, match.DueDate))
            .ToHashSet();

        return NetWorthForecastCalculator.Compute(current, today, items, matchedOccurrences);
    }

    private static AccountBalance ToBalance(
        Account account,
        decimal? balance,
        decimal? balanceEur
    ) =>
        new(
            account.Id,
            account.Name,
            account.Provider,
            account.Type,
            account.Currency,
            balance,
            balanceEur,
            balanceEur is null ? null : account.BalanceUpdatedAt
        );

    /// <summary>Month-end dates for the trailing window, ending with today.</summary>
    private static IReadOnlyList<DateOnly> BuildSampleDates(int months, DateOnly today)
    {
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
