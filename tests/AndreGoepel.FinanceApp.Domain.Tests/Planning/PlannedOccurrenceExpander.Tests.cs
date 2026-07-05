using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Domain.Tests.Planning;

public class PlannedOccurrenceExpanderTests
{
    [Fact]
    public void Expand_OneTime_InWindow_ReturnsThatDate()
    {
        var schedule = new PlannedSchedule(PlannedFrequency.OneTime, new DateOnly(2026, 3, 10));

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)
        );

        Assert.Equal([new DateOnly(2026, 3, 10)], dates);
    }

    [Fact]
    public void Expand_OneTime_OutsideWindow_IsEmpty()
    {
        var schedule = new PlannedSchedule(PlannedFrequency.OneTime, new DateOnly(2026, 2, 10));

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)
        );

        Assert.Empty(dates);
    }

    [Fact]
    public void Expand_Monthly_ReturnsOneDatePerMonthOnTheAnchorDay()
    {
        var schedule = new PlannedSchedule(PlannedFrequency.Monthly, new DateOnly(2026, 1, 15));

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31)
        );

        Assert.Equal(
            [new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 15)],
            dates
        );
    }

    [Fact]
    public void Expand_Monthly_MonthEndAnchor_DoesNotDrift()
    {
        // Start on the 31st: Feb clamps to 28, but March returns to 31 (anchored on start).
        var schedule = new PlannedSchedule(PlannedFrequency.Monthly, new DateOnly(2026, 1, 31));

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31)
        );

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)],
            dates
        );
    }

    [Fact]
    public void Expand_Quarterly_StepsByThreeMonths()
    {
        var schedule = new PlannedSchedule(PlannedFrequency.Quarterly, new DateOnly(2026, 1, 10));

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)
        );

        Assert.Equal(
            [
                new DateOnly(2026, 1, 10),
                new DateOnly(2026, 4, 10),
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 10, 10),
            ],
            dates
        );
    }

    [Fact]
    public void Expand_RespectsEndDate()
    {
        var schedule = new PlannedSchedule(
            PlannedFrequency.Monthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 20)
        );

        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)
        );

        Assert.Equal([new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15)], dates);
    }
}
