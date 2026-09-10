using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Insights;

namespace AndreGoepel.FinanceApp.Tests.Insights;

public sealed class NetWorthAccountSelectionTests
{
    [Fact]
    public void Create_UsesAllRetainedAccountsForReportingButOnlyActiveAccountsForCards()
    {
        var active = CreateAccount(AccountStatus.Active);
        var deactivated = CreateAccount(AccountStatus.Deactivated);

        var selection = NetWorthAccountSelection.Create([active, deactivated]);

        Assert.Equal([active, deactivated], selection.ReportingAccounts);
        Assert.Contains(active.Id, selection.BalanceCardAccountIds);
        Assert.DoesNotContain(deactivated.Id, selection.BalanceCardAccountIds);
    }

    private static Account CreateAccount(AccountStatus status) =>
        new()
        {
            Name = "Wise EUR",
            Provider = ProviderKind.Wise,
            Type = AccountType.MultiCurrency,
            Currency = "EUR",
            SyncMethod = SyncMethod.Api,
            Status = status,
        };
}
