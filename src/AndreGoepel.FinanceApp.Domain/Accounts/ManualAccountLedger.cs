namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Keeps a manually maintained account's balance anchor equal to its ledger
/// balance — opening balance plus every entry — so the dashboards and net worth
/// read the right figure without anyone re-counting the cash. The anchor moves to
/// "now" with each entry; net-worth history reconstructs earlier balances from
/// the entries dated after each sample date, back-dated ones included.
/// </summary>
internal static class ManualAccountLedger
{
    /// <summary>
    /// Shifts the anchor by one entry (pass negated amounts to take one back). An
    /// account that never had a balance starts at zero. The EUR anchor follows the
    /// native one on EUR accounts; on other currencies it is only kept while every
    /// entry's EUR value is known, otherwise it is dropped so net worth does not
    /// silently carry a stale figure.
    /// </summary>
    public static void Move(Account account, decimal amount, decimal? amountEur, DateTimeOffset now)
    {
        var previousEur = account.CurrentBalance is null ? 0m : account.CurrentBalanceEur;
        account.CurrentBalance = (account.CurrentBalance ?? 0m) + amount;
        account.CurrentBalanceEur = IsEur(account)
            ? account.CurrentBalance
            : previousEur + amountEur;
        account.BalanceUpdatedAt = now;
    }

    public static bool IsEur(Account account) =>
        string.Equals(account.Currency, "EUR", StringComparison.OrdinalIgnoreCase);
}
