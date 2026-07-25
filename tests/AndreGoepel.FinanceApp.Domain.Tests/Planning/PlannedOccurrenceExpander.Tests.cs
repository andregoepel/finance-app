using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Domain.Tests.Planning;

public sealed class PlannedOccurrenceExpanderTests
{
    [Fact]
    public void Expand_OneTime_InWindow_ReturnsThatDate()
    {
        // Arrange
        var schedule = new PlannedSchedule(PlannedFrequency.OneTime, new DateOnly(2026, 3, 10));

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)
        );

        // Assert
        Assert.Equal([new DateOnly(2026, 3, 10)], dates);
    }

    [Fact]
    public void Expand_OneTime_OutsideWindow_IsEmpty()
    {
        // Arrange
        var schedule = new PlannedSchedule(PlannedFrequency.OneTime, new DateOnly(2026, 2, 10));

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)
        );

        // Assert
        Assert.Empty(dates);
    }

    [Fact]
    public void Expand_Monthly_ReturnsOneDatePerMonthOnTheAnchorDay()
    {
        // Arrange
        var schedule = new PlannedSchedule(PlannedFrequency.Monthly, new DateOnly(2026, 1, 15));

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31)
        );

        // Assert
        Assert.Equal(
            [new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 15)],
            dates
        );
    }

    [Fact]
    public void Expand_Monthly_MonthEndAnchor_DoesNotDrift()
    {
        // Arrange — start on the 31st: Feb clamps to 28, but March returns to 31 (anchored on start).
        var schedule = new PlannedSchedule(PlannedFrequency.Monthly, new DateOnly(2026, 1, 31));

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31)
        );

        // Assert
        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)],
            dates
        );
    }

    [Fact]
    public void Expand_Quarterly_StepsByThreeMonths()
    {
        // Arrange
        var schedule = new PlannedSchedule(PlannedFrequency.Quarterly, new DateOnly(2026, 1, 10));

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)
        );

        // Assert
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
    public void Expand_MonthlyWithEndDate_StopsAtEndDate()
    {
        // Arrange
        var schedule = new PlannedSchedule(
            PlannedFrequency.Monthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 20)
        );

        // Act
        var dates = PlannedOccurrenceExpander.Expand(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)
        );

        // Assert
        Assert.Equal([new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15)], dates);
    }
}
