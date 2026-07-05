using AndreGoepel.FinanceApp.Components.Pages;
using AndreGoepel.FinanceApp.Insights;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Tests.Components.Pages;

public class DashboardTests : BunitContext
{
    private void RegisterDashboardService(MonthlyOverview overview)
    {
        var service = Substitute.For<IDashboardService>();
        service
            .GetMonthlyOverviewAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(overview));
        Services.AddSingleton(service);

        var netWorth = Substitute.For<INetWorthService>();
        netWorth
            .GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new NetWorthOverview(0m, [], 0)));
        Services.AddSingleton(netWorth);
    }

    [Fact]
    public void Render_ShowsHeadingTotalsAndSectionCards()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterDashboardService(new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0));

        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("Income", cut.Markup);
        Assert.Contains("Spending by category", cut.Markup);
        Assert.Contains("Net worth", cut.Markup);
        Assert.Contains("Budgets", cut.Markup);
    }

    [Fact]
    public void Render_WithExpenses_ListsTheCategory()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterDashboardService(
            new MonthlyOverview(
                Income: 2000m,
                Expenses: 150m,
                Net: 1850m,
                SpendingByCategory: [new CategorySpend("Groceries", 150m)],
                Budgets: [],
                UnconvertedCount: 0,
                UncategorizedCount: 0
            )
        );

        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("Groceries", cut.Markup);
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
