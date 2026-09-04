using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;

namespace AndreGoepel.FinanceApp.Tests.Connections;

public class WiseBalanceServiceTests
{
    [Theory]
    [InlineData(null, "EUR", "Wise EUR")]
    [InlineData("", "USD", "Wise USD")]
    [InlineData("  ", "GBP", "Wise GBP")]
    [InlineData("Vacation", "EUR", "Vacation")]
    [InlineData("  Urlaub  ", "EUR", "Urlaub")]
    public void AccountNameFor_JarsKeepTheirName_StandardBalancesGetCurrencyName(
        string? jarName,
        string currency,
        string expected
    )
    {
        // Arrange
        var balance = new WiseBalance(1, currency, 100m, Name: jarName);

        // Act / Assert
        Assert.Equal(expected, WiseBalanceService.AccountNameFor(balance));
    }
}
