namespace AndreGoepel.FinanceApp.Domain.NetWorth;

/// <summary>
/// Reconstructs total net worth (EUR) at a set of dates from each account's
/// balance anchor and its transactions. For one account,
/// <c>balance(D) = AnchorEur + S(D) − S(anchorDate)</c> where <c>S(x)</c> is the
/// cumulative EUR sum of the account's transactions up to <c>x</c>. Net worth at
/// a date is the sum across accounts. Pure and side-effect-free.
/// </summary>
public static class NetWorthCalculator
{
    public static IReadOnlyList<NetWorthPoint> Compute(
        IReadOnlyList<AccountAnchor> accounts,
        IReadOnlyList<DateOnly> sampleDates
    )
    {
        return sampleDates
            .Select(date => new NetWorthPoint(
                date,
                accounts.Sum(account => BalanceAt(account, date))
            ))
            .ToList();
    }

    private static decimal BalanceAt(AccountAnchor account, DateOnly date) =>
        account.AnchorEur
        + CumulativeUpTo(account, date)
        - CumulativeUpTo(account, account.AnchorDate);

    private static decimal CumulativeUpTo(AccountAnchor account, DateOnly date) =>
        account.Transactions.Where(t => t.Date <= date).Sum(t => t.AmountEur);
}

/// <summary>
/// One account's net-worth inputs: its EUR balance anchor as of
/// <paramref name="AnchorDate"/>, and its EUR-valued transactions.
/// </summary>
public sealed record AccountAnchor(
    decimal AnchorEur,
    DateOnly AnchorDate,
    IReadOnlyList<(DateOnly Date, decimal AmountEur)> Transactions
);

/// <summary>Total net worth (EUR) at a point in time.</summary>
public sealed record NetWorthPoint(DateOnly Date, decimal Amount);
