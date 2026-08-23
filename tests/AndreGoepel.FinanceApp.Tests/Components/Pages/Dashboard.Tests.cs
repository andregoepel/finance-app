using AndreGoepel.FinanceApp.Components.Pages;
using AndreGoepel.FinanceApp.Insights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.FinanceApp.Tests.Components.Pages;

public sealed class DashboardTests : LocalizedTestContext
{
    private void RegisterDashboardService(
        MonthlyOverview overview,
        CryptoOverview? cryptoOverview = null
    )
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

        var planning = Substitute.For<AndreGoepel.FinanceApp.Planning.IPlanningService>();
        planning
            .GetUpcomingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<
                    IReadOnlyList<AndreGoepel.FinanceApp.Domain.Planning.PlannedOccurrence>
                >([])
            );
        Services.AddSingleton(planning);

        var crypto = Substitute.For<ICryptoService>();
        crypto
            .GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cryptoOverview ?? new CryptoOverview(0m, [], null)));
        Services.AddSingleton(crypto);
    }

    [Fact]
    public void Render_DefaultOverview_ShowsHeadingTotalsAndSectionCards()
    {
        // Arrange
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

    /// <summary>
    /// The counterpart to the English render above: the same page under the German culture. This is
    /// the first end-to-end proof that the whole chain — request culture, the injected
    /// <c>IStringLocalizer&lt;Strings&gt;</c>, and the embedded <c>.de.resx</c> — actually swaps the
    /// rendered copy, rather than each key merely resolving correctly in isolation.
    /// </summary>
    [Fact]
    public void Render_UnderGermanCulture_ShowsGermanHeadingTotalsAndSectionCards()
    {
        // Arrange
        using var culture = UseCulture("de");
        RegisterDashboardService(new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0));

        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("Übersicht", cut.Markup);
        Assert.Contains("Einnahmen", cut.Markup);
        Assert.Contains("Ausgaben nach Kategorie", cut.Markup);
        Assert.Contains("Nettovermögen", cut.Markup);
        Assert.DoesNotContain("Spending by category", cut.Markup);
    }

    [Fact]
    public void Render_WithBudget_ShowsBudgetProgress()
    {
        // Arrange — no spending list (keeps RadzenChart out of bUnit), one budget.
        RegisterDashboardService(
            new MonthlyOverview(
                Income: 2000m,
                Expenses: 150m,
                Net: 1850m,
                SpendingByCategory: [],
                Budgets: [new BudgetProgress("Groceries", 400m, 150m)],
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
    public void Render_WithCryptoPositions_ShowsCryptoTile()
    {
        // Arrange
        RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            new CryptoOverview(
                TotalEur: 47_500m,
                Positions:
                [
                    new CryptoPosition(
                        Guid.NewGuid(),
                        "Crypto.com",
                        "BTC",
                        "bitcoin",
                        0.5m,
                        95_000m,
                        47_500m
                    ),
                ],
                OldestPriceAt: DateTimeOffset.UtcNow
            )
        );

        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("Crypto", cut.Markup);
        Assert.Contains("BTC", cut.Markup);
        Assert.Contains($"{47_500m:N2} €", cut.Markup); // same format the page uses
    }

    [Fact]
    public void Render_WithoutCryptoPositions_HidesCryptoTile()
    {
        // Arrange
        RegisterDashboardService(new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0));

        // Act
        var cut = Render<Dashboard>();

        // Assert — no crypto section without holdings.
        Assert.DoesNotContain("settings/crypto", cut.Markup);
    }

    [Fact]
    public void Route_DashboardPage_IsRootAndRequiresAuthorization()
    {
        // Act
        var route = Attribute.GetCustomAttribute(typeof(Dashboard), typeof(RouteAttribute));
        var authorize = Attribute.GetCustomAttribute(typeof(Dashboard), typeof(AuthorizeAttribute));

        // Assert
        Assert.Equal("/", Assert.IsType<RouteAttribute>(route).Template);
        Assert.NotNull(authorize);
    }
}
