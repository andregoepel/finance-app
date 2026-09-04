using AndreGoepel.FinanceApp.Components.Settings;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Tests.Components.Settings;

public sealed class AccountListPresentationTests
{
    [Fact]
    public void Build_KeepsNonWiseAccountsFlatAndGroupsWiseByConnection()
    {
        var firstConnection = Connection("André – Wise");
        var secondConnection = Connection("Dani – Wise");
        var accounts = new[]
        {
            Account("DKB", ProviderKind.Dkb),
            Account("Wise USD", ProviderKind.Wise, firstConnection.Id),
            Account("Wise EUR", ProviderKind.Wise, firstConnection.Id),
            Account("Wise GBP", ProviderKind.Wise, secondConnection.Id),
        };

        var result = AccountListPresentation.Build(
            accounts,
            [secondConnection, firstConnection],
            "Unlinked accounts"
        );

        Assert.Equal(["DKB"], result.NonWiseAccounts.Select(account => account.Name));
        Assert.Collection(
            result.WiseGroups,
            group =>
            {
                Assert.Equal("André – Wise", group.Label);
                Assert.Equal(
                    ["Wise EUR", "Wise USD"],
                    group.Accounts.Select(account => account.Name)
                );
            },
            group =>
            {
                Assert.Equal("Dani – Wise", group.Label);
                Assert.Equal(["Wise GBP"], group.Accounts.Select(account => account.Name));
            }
        );
    }

    [Fact]
    public void Build_CollectsMissingAndUnknownWiseConnectionsInUnlinkedGroup()
    {
        var missingConnectionId = Guid.NewGuid();
        var accounts = new[]
        {
            Account("Wise USD", ProviderKind.Wise, missingConnectionId),
            Account("Wise EUR", ProviderKind.Wise),
        };

        var result = AccountListPresentation.Build(accounts, [], "Unlinked accounts");

        var group = Assert.Single(result.WiseGroups);
        Assert.Null(group.ConnectionId);
        Assert.Equal("Unlinked accounts", group.Label);
        Assert.Equal(["Wise EUR", "Wise USD"], group.Accounts.Select(account => account.Name));
    }

    private static ProviderConnection Connection(string label) =>
        new() { Provider = ProviderKind.Wise, Label = label };

    private static Account Account(string name, ProviderKind provider, Guid? connectionId = null) =>
        new()
        {
            Name = name,
            Provider = provider,
            Type = AccountType.Checking,
            Currency = "EUR",
            SyncMethod = SyncMethod.Api,
            ConnectionId = connectionId,
        };
}
