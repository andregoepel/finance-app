using AndreGoepel.FinanceApp.Components.Import.Pages;
using AndreGoepel.FinanceApp.Components.Settings.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TransactionsPage = AndreGoepel.FinanceApp.Components.Transactions.Pages.Transactions;

namespace AndreGoepel.FinanceApp.Tests.Components;

public class PageRoutingTests
{
    [Theory]
    [InlineData(typeof(TransactionsPage), "/transactions")]
    [InlineData(typeof(Import), "/import")]
    [InlineData(typeof(Accounts), "/settings/accounts")]
    [InlineData(typeof(Categories), "/settings/categories")]
    [InlineData(typeof(CryptoHoldings), "/settings/crypto")]
    public void Page_HasExpectedRoute_AndRequiresAuthorization(Type pageType, string route)
    {
        // Act
        var routeAttribute = Attribute.GetCustomAttribute(pageType, typeof(RouteAttribute));
        var authorizeAttribute = Attribute.GetCustomAttribute(pageType, typeof(AuthorizeAttribute));

        // Assert
        Assert.Equal(route, Assert.IsType<RouteAttribute>(routeAttribute).Template);
        Assert.NotNull(authorizeAttribute);
    }
}
