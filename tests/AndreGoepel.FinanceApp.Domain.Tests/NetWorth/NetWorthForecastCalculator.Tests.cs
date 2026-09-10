using AndreGoepel.FinanceApp.Domain.NetWorth;
using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Domain.Tests.NetWorth;

public sealed class NetWorthForecastCalculatorTests
{
    [Fact]
    public void Compute_ProjectsActiveUnmatchedPlansAcrossTwelveMonthEnds()
    {
        var today = new DateOnly(2026, 9, 4);
        var salary = Create(2_000m, PlannedFrequency.Monthly, new DateOnly(2026, 9, 25));
        var rent = Create(-800m, PlannedFrequency.Monthly, new DateOnly(2026, 9, 10));
        var inactive = Create(-10_000m, PlannedFrequency.Monthly, today, active: false);
        var matched = new HashSet<(Guid, DateOnly)> { (rent.Id, new DateOnly(2026, 9, 10)) };

        var result = NetWorthForecastCalculator.Compute(
            10_000m,
            today,
            [salary, rent, inactive],
            matched
        );

        Assert.Equal(13, result.Count);
        Assert.Equal(new NetWorthPoint(today, 10_000m), result[0]);
        Assert.Equal(12_000m, result[1].Amount);
        Assert.Equal(13_200m, result[2].Amount);
        Assert.Equal(25_200m, result[^1].Amount);
    }

    [Fact]
    public void Compute_IncludesOneTimeItemsOnlyOnceAndHonorsEndDates()
    {
        var today = new DateOnly(2026, 9, 4);
        var oneTime = Create(500m, PlannedFrequency.OneTime, new DateOnly(2026, 10, 5));
        var ending = Create(
            -100m,
            PlannedFrequency.Monthly,
            new DateOnly(2026, 9, 5),
            endDate: new DateOnly(2026, 10, 5)
        );

        var result = NetWorthForecastCalculator.Compute(
            1_000m,
            today,
            [oneTime, ending],
            new HashSet<(Guid, DateOnly)>()
        );

        Assert.Equal(900m, result[1].Amount);
        Assert.All(result.Skip(2), point => Assert.Equal(1_300m, point.Amount));
    }

    [Fact]
    public void Compute_UsesOnlyCurrentBudgetRemainderThenFullFutureBudgets()
    {
        var today = new DateOnly(2026, 9, 20);
        var groceries = Guid.NewGuid();
        var budget = new Domain.Budgets.Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = groceries,
            MonthlyLimit = 600m,
            StartMonth = new DateOnly(2026, 9, 1),
        };

        var result = NetWorthForecastCalculator.Compute(
            10_000m,
            today,
            [],
            new HashSet<(Guid, DateOnly)>(),
            new Dictionary<Guid, Guid?> { [groceries] = null },
            [budget],
            [(groceries, 570m)],
            months: 2
        );

        Assert.Equal(9_970m, result[1].Amount);
        Assert.Equal(9_370m, result[2].Amount);
    }

    [Fact]
    public void Compute_DoesNotDoubleCountPlannedExpenseInsideBudget()
    {
        var today = new DateOnly(2026, 9, 20);
        var groceries = Guid.NewGuid();
        var planned = Create(-80m, PlannedFrequency.OneTime, new DateOnly(2026, 9, 25));
        planned.CategoryId = groceries;
        var budget = new Domain.Budgets.Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = groceries,
            MonthlyLimit = 600m,
            StartMonth = new DateOnly(2026, 9, 1),
            EndMonth = new DateOnly(2026, 9, 1),
        };

        var result = NetWorthForecastCalculator.Compute(
            10_000m,
            today,
            [planned],
            new HashSet<(Guid, DateOnly)>(),
            new Dictionary<Guid, Guid?> { [groceries] = null },
            [budget],
            [(groceries, 570m)],
            months: 1
        );

        Assert.Equal(9_920m, result[1].Amount);
    }

    private static PlannedItem Create(
        decimal amount,
        PlannedFrequency frequency,
        DateOnly startDate,
        bool active = true,
        DateOnly? endDate = null
    ) =>
        new()
        {
            Description = "Plan",
            Amount = amount,
            Schedule = new PlannedSchedule(frequency, startDate, endDate),
            Active = active,
        };
}
