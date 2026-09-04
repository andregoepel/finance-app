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

    /// <summary>One account's EUR balance at <paramref name="date"/>.</summary>
    public static decimal BalanceAt(AccountAnchor account, DateOnly date) =>
        BalanceAt(account.AnchorEur, account.AnchorDate, account.Transactions, date);

    /// <summary>
    /// The currency-agnostic core: a balance known as of <paramref name="anchorDate"/>,
    /// moved to <paramref name="date"/> by the transactions in between. Transactions
    /// dated on the anchor day count as already reflected in the anchor. The same
    /// formula serves the EUR series and an account's native-currency balance.
    /// </summary>
    public static decimal BalanceAt(
        decimal anchor,
        DateOnly anchorDate,
        IReadOnlyList<(DateOnly Date, decimal Amount)> transactions,
        DateOnly date
    ) => anchor + CumulativeUpTo(transactions, date) - CumulativeUpTo(transactions, anchorDate);

    private static decimal CumulativeUpTo(
        IReadOnlyList<(DateOnly Date, decimal Amount)> transactions,
        DateOnly date
    ) => transactions.Where(t => t.Date <= date).Sum(t => t.Amount);
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
