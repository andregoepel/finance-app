using AndreGoepel.FinanceApp.Domain.NetWorth;

namespace AndreGoepel.FinanceApp.Domain.Tests.NetWorth;

public sealed class NetWorthCalculatorTests
{
    [Fact]
    public void Compute_TransactionBeforeAnchor_ReconstructsBalanceBackward()
    {
        // Arrange — anchor 1000 EUR as of Mar 31; a -200 expense on Mar 10.
        // On Mar 31 the balance is 1000; before Mar 10 it was 1000 - (-200) = 1200.
        var account = new AccountAnchor(
            AnchorEur: 1000m,
            AnchorDate: new DateOnly(2026, 3, 31),
            Transactions: [(new DateOnly(2026, 3, 10), -200m)]
        );
        var dates = new[]
        {
            new DateOnly(2026, 3, 5), // before the expense
            new DateOnly(2026, 3, 31), // the anchor
        };

        // Act
        var series = NetWorthCalculator.Compute([account], dates);

        // Assert
        Assert.Equal(1200m, series[0].Amount);
        Assert.Equal(1000m, series[1].Amount);
    }

    [Fact]
    public void Compute_TransactionAfterAnchor_ProjectsBalanceForward()
    {
        // Arrange — anchor 500 as of Mar 1; +300 income on Mar 15.
        var account = new AccountAnchor(
            AnchorEur: 500m,
            AnchorDate: new DateOnly(2026, 3, 1),
            Transactions: [(new DateOnly(2026, 3, 15), 300m)]
        );

        // Act
        var series = NetWorthCalculator.Compute([account], [new DateOnly(2026, 3, 31)]);

        // Assert — after the income, balance is 800.
        Assert.Equal(800m, Assert.Single(series).Amount);
    }

    [Fact]
    public void Compute_MultipleAccounts_SumsAcrossAccounts()
    {
        // Arrange
        var a = new AccountAnchor(1000m, new DateOnly(2026, 3, 31), []);
        var b = new AccountAnchor(250m, new DateOnly(2026, 3, 31), []);

        // Act
        var series = NetWorthCalculator.Compute([a, b], [new DateOnly(2026, 3, 31)]);

        // Assert
        Assert.Equal(1250m, Assert.Single(series).Amount);
    }
}
