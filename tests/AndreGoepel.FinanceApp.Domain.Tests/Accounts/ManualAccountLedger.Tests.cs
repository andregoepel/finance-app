using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Domain.Tests.Accounts;

public sealed class ManualAccountLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static Account CashAccount(
        string currency = "EUR",
        decimal? balance = null,
        decimal? balanceEur = null
    ) =>
        new()
        {
            Name = "Cash",
            Provider = ProviderKind.Cash,
            Type = AccountType.Cash,
            Currency = currency,
            SyncMethod = SyncMethod.Manual,
            CurrentBalance = balance,
            CurrentBalanceEur = balanceEur,
        };

    [Fact]
    public void Move_EurAccountWithoutBalance_StartsAtZeroAndMirrorsEur()
    {
        // Arrange
        var account = CashAccount();

        // Act
        ManualAccountLedger.Move(account, -12.5m, -12.5m, Now);

        // Assert
        Assert.Equal(-12.5m, account.CurrentBalance);
        Assert.Equal(-12.5m, account.CurrentBalanceEur);
        Assert.Equal(Now, account.BalanceUpdatedAt);
    }

    [Fact]
    public void Move_EurAccount_AddsToTheOpeningBalance()
    {
        // Arrange
        var account = CashAccount(balance: 50m, balanceEur: 50m);

        // Act
        ManualAccountLedger.Move(account, -12.5m, -12.5m, Now);

        // Assert
        Assert.Equal(37.5m, account.CurrentBalance);
        Assert.Equal(37.5m, account.CurrentBalanceEur);
    }

    [Fact]
    public void Move_NegatedAmounts_TakeAnEntryBack()
    {
        // Arrange
        var account = CashAccount(balance: 37.5m, balanceEur: 37.5m);

        // Act
        ManualAccountLedger.Move(account, 12.5m, 12.5m, Now);

        // Assert
        Assert.Equal(50m, account.CurrentBalance);
        Assert.Equal(50m, account.CurrentBalanceEur);
    }

    [Fact]
    public void Move_ForeignAccountWithKnownEurValue_MovesBothAnchors()
    {
        // Arrange
        var account = CashAccount("CHF", balance: 100m, balanceEur: 104m);

        // Act
        ManualAccountLedger.Move(account, -10m, -10.4m, Now);

        // Assert
        Assert.Equal(90m, account.CurrentBalance);
        Assert.Equal(93.6m, account.CurrentBalanceEur);
    }

    [Fact]
    public void Move_ForeignAccountWithoutEurValue_DropsTheEurAnchor()
    {
        // Arrange — a stale EUR figure would silently skew net worth.
        var account = CashAccount("CHF", balance: 100m, balanceEur: 104m);

        // Act
        ManualAccountLedger.Move(account, -10m, null, Now);

        // Assert
        Assert.Equal(90m, account.CurrentBalance);
        Assert.Null(account.CurrentBalanceEur);
    }

    [Fact]
    public void Move_ForeignAccountWithoutBalance_StartsTheEurAnchorFromTheEntry()
    {
        // Arrange
        var account = CashAccount("CHF");

        // Act
        ManualAccountLedger.Move(account, 20m, 20.8m, Now);

        // Assert
        Assert.Equal(20m, account.CurrentBalance);
        Assert.Equal(20.8m, account.CurrentBalanceEur);
    }

    [Fact]
    public void Move_ForeignAccountAnchoredWithoutEur_StaysWithoutEur()
    {
        // Arrange — the balance was set while no rate was available; one entry
        // with a known EUR value cannot make up for the unknown rest.
        var account = CashAccount("CHF", balance: 100m, balanceEur: null);

        // Act
        ManualAccountLedger.Move(account, -10m, -10.4m, Now);

        // Assert
        Assert.Equal(90m, account.CurrentBalance);
        Assert.Null(account.CurrentBalanceEur);
    }

    [Theory]
    [InlineData("EUR", true)]
    [InlineData("eur", true)]
    [InlineData("CHF", false)]
    public void IsEur_ComparesCaseInsensitively(string currency, bool expected)
    {
        // Act
        var result = ManualAccountLedger.IsEur(CashAccount(currency));

        // Assert
        Assert.Equal(expected, result);
    }
}
