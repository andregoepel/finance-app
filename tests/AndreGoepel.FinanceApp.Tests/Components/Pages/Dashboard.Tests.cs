using AndreGoepel.FinanceApp.Components.Pages;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace AndreGoepel.FinanceApp.Tests.Components.Pages;

public class DashboardTests : BunitContext
{
    [Fact]
    public void Render_ShowsDashboardHeadingAndEmptyStateCards()
    {
        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("Net worth", cut.Markup);
        Assert.Contains("Spending by category", cut.Markup);
        Assert.Contains("Budgets", cut.Markup);
    }

    [Fact]
    public void Route_IsRootAndRequiresAuthorization()
    {
        // Act
        var route = Attribute.GetCustomAttribute(typeof(Dashboard), typeof(RouteAttribute));
        var authorize = Attribute.GetCustomAttribute(typeof(Dashboard), typeof(AuthorizeAttribute));

        // Assert
        Assert.Equal("/", Assert.IsType<RouteAttribute>(route).Template);
        Assert.NotNull(authorize);
    }
}
