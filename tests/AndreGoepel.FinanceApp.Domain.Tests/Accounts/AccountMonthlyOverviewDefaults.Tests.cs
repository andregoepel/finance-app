using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Domain.Tests.Accounts;

public sealed class AccountMonthlyOverviewDefaultsTests
{
    [Fact]
    public void NewAccount_IsIncludedInMonthlyOverviewByDefault()
    {
        var account = new Account
        {
            Name = "Household",
            Provider = ProviderKind.Dkb,
            Type = AccountType.Checking,
            Currency = "EUR",
            SyncMethod = SyncMethod.Api,
        };

        Assert.True(account.IncludeInMonthlyOverviewByDefault);
    }
}
