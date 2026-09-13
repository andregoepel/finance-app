using AndreGoepel.FinanceApp.Components.Pages;
using AndreGoepel.FinanceApp.Components.Shared;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Insights;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;
using Radzen.Blazor;

namespace AndreGoepel.FinanceApp.Tests.Components.Pages;

public sealed class DashboardTests : LocalizedTestContext
{
    private IDashboardService RegisterDashboardService(
        MonthlyOverview overview,
        CryptoOverview? cryptoOverview = null,
        NetWorthOverview? netWorthOverview = null,
        IReadOnlyList<MonthlyAccountOption>? monthlyAccounts = null
    )
    {
        var service = Substitute.For<IDashboardService>();
        service
            .GetMonthlyOverviewAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyCollection<Guid>?>()
            )
            .Returns(Task.FromResult(overview));
        service
            .GetMonthlyAccountOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(monthlyAccounts ?? (IReadOnlyList<MonthlyAccountOption>)[]));
        Services.AddSingleton(service);

        var netWorth = Substitute.For<INetWorthService>();
        netWorth
            .GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(netWorthOverview ?? new NetWorthOverview(0m, [], 0, [])));
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

        return service;
    }

    [Fact]
    public void Render_WithMonthlyAccountDefaults_RequestsOverviewForDefaultSelection()
    {
        var included = new MonthlyAccountOption(Guid.NewGuid(), "Household", true);
        var excluded = new MonthlyAccountOption(Guid.NewGuid(), "Savings", false);
        var service = RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            monthlyAccounts: [included, excluded]
        );

        var cut = Render<Dashboard>();

        Assert.NotNull(cut.Find("[data-testid='monthly-account-filter']"));
        service
            .Received(1)
            .GetMonthlyOverviewAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(included.Id) && !ids.Contains(excluded.Id)
                )
            );
    }

    [Fact]
    public async Task MonthlyAccountOverride_IsKeptWhenMonthChanges()
    {
        var first = new MonthlyAccountOption(Guid.NewGuid(), "Household", true);
        var second = new MonthlyAccountOption(Guid.NewGuid(), "Savings", false);
        var service = RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            monthlyAccounts: [first, second]
        );
        var cut = Render<Dashboard>();
        var dropdown = cut.FindComponent<RadzenDropDown<IEnumerable<Guid>>>();
        var overridden = (IEnumerable<Guid>)[second.Id];

        await cut.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync(overridden));
        await cut.InvokeAsync(() => dropdown.Instance.Change.InvokeAsync(overridden));
        var navigator = cut.FindComponent<MonthNavigator>();
        await cut.InvokeAsync(() =>
            navigator.Instance.MonthChanged.InvokeAsync(new DateOnly(2026, 8, 1))
        );

        await service
            .Received()
            .GetMonthlyOverviewAsync(
                2026,
                8,
                Arg.Any<CancellationToken>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(second.Id) && !ids.Contains(first.Id)
                )
            );
    }

    [Fact]
    public async Task MonthlyAccountOverride_AllowsEmptySelection()
    {
        var account = new MonthlyAccountOption(Guid.NewGuid(), "Household", true);
        var service = RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            monthlyAccounts: [account]
        );
        var cut = Render<Dashboard>();
        var dropdown = cut.FindComponent<RadzenDropDown<IEnumerable<Guid>>>();
        var empty = (IEnumerable<Guid>)[];

        await cut.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync(empty));
        await cut.InvokeAsync(() => dropdown.Instance.Change.InvokeAsync(empty));

        await service
            .Received()
            .GetMonthlyOverviewAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0)
            );
    }

    [Fact]
    public void NewDashboardInstance_RestoresPersistedAccountDefaults()
    {
        var included = new MonthlyAccountOption(Guid.NewGuid(), "Household", true);
        var excluded = new MonthlyAccountOption(Guid.NewGuid(), "Savings", false);
        var service = RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            monthlyAccounts: [included, excluded]
        );

        using var firstVisit = Render<Dashboard>();
        using var reloadedVisit = Render<Dashboard>();

        service
            .Received(2)
            .GetMonthlyOverviewAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 1 && ids.Contains(included.Id) && !ids.Contains(excluded.Id)
                )
            );
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
    public void Render_WithAccounts_GroupsByTypeWithSubtotalsAndLinksToTransactions()
    {
        // Arrange — two checking accounts (one in PHP) and a cash account; the
        // total is the net-worth figure, the group subtotals add up the EUR balances.
        var checkingId = Guid.NewGuid();
        var overview = new NetWorthOverview(
            Current: 1_650m,
            Series: [],
            AccountsWithoutBalance: 0,
            Accounts:
            [
                new AccountBalance(
                    checkingId,
                    "Main EUR",
                    ProviderKind.Wise,
                    AccountType.Checking,
                    "EUR",
                    Balance: 1_000m,
                    BalanceEur: 1_000m,
                    AsOf: new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
                ),
                new AccountBalance(
                    Guid.NewGuid(),
                    "Pesos",
                    ProviderKind.Wise,
                    AccountType.Checking,
                    "PHP",
                    Balance: 33_000m,
                    BalanceEur: 500m,
                    AsOf: new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
                ),
                new AccountBalance(
                    Guid.NewGuid(),
                    "Wallet",
                    ProviderKind.Cash,
                    AccountType.Cash,
                    "EUR",
                    Balance: 150m,
                    BalanceEur: 150m,
                    AsOf: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)
                ),
            ]
        );
        RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            netWorthOverview: overview
        );

        // Act
        var cut = Render<Dashboard>();

        // Assert — group headings, per-account rows, the foreign-currency figure,
        // the checking subtotal, the total, and a link into the filtered list.
        Assert.Contains("Checking", cut.Markup);
        Assert.Contains("Cash", cut.Markup);
        Assert.Contains("Main EUR", cut.Markup);
        Assert.Contains("Pesos", cut.Markup);
        Assert.Contains($"{33_000m:N2} PHP", cut.Markup);
        Assert.Contains($"{1_500m:N2} €", cut.Markup);
        Assert.Contains($"{1_650m:N2} €", cut.Markup);
        Assert.Contains($"transactions?account={checkingId}", cut.Markup);
    }

    [Fact]
    public void Render_WithAccountWithoutBalance_ListsItWithSetOneHint()
    {
        // Arrange
        var overview = new NetWorthOverview(
            Current: 0m,
            Series: [],
            AccountsWithoutBalance: 1,
            Accounts:
            [
                new AccountBalance(
                    Guid.NewGuid(),
                    "DKB Giro",
                    ProviderKind.Dkb,
                    AccountType.Checking,
                    "EUR",
                    Balance: null,
                    BalanceEur: null,
                    AsOf: null
                ),
            ]
        );
        RegisterDashboardService(
            new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0),
            netWorthOverview: overview
        );

        // Act
        var cut = Render<Dashboard>();

        // Assert — the account is not hidden; it carries the hint instead of a figure.
        Assert.Contains("DKB Giro", cut.Markup);
        Assert.Contains("no balance yet", cut.Markup);
    }

    [Fact]
    public void Render_WithoutAccounts_ShowsAddOneHint()
    {
        // Arrange
        RegisterDashboardService(new MonthlyOverview(0m, 0m, 0m, [], [], 0, 0));

        // Act
        var cut = Render<Dashboard>();

        // Assert
        Assert.Contains("No accounts yet", cut.Markup);
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
