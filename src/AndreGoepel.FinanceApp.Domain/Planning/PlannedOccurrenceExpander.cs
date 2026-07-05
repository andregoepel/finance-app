namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Expands a planned item's schedule into concrete due dates within a window.
/// Recurring occurrences step from the start date by whole months (anchored on the
/// start day, so month-ends clamp naturally), stopping at the end date. Pure.
/// </summary>
public static class PlannedOccurrenceExpander
{
    private const int MaxSteps = 5000; // guard against runaway loops

    public static IReadOnlyList<DateOnly> Expand(
        PlannedSchedule schedule,
        DateOnly from,
        DateOnly to
    )
    {
        if (schedule.Frequency == PlannedFrequency.OneTime)
        {
            return schedule.StartDate >= from && schedule.StartDate <= to
                ? [schedule.StartDate]
                : [];
        }

        var step = schedule.Frequency switch
        {
            PlannedFrequency.Monthly => 1,
            PlannedFrequency.Quarterly => 3,
            PlannedFrequency.Yearly => 12,
            _ => 0,
        };
        if (step == 0)
        {
            return [];
        }

        var dates = new List<DateOnly>();
        for (var k = 0; k < MaxSteps; k++)
        {
            // Anchor each occurrence off the start date so the day-of-month does
            // not drift after a month-end clamp.
            var due = schedule.StartDate.AddMonths(k * step);
            if (due > to || (schedule.EndDate is DateOnly end && due > end))
            {
                break;
            }
            if (due >= from)
            {
                dates.Add(due);
            }
        }
        return dates;
    }
}

/// <summary>One expanded occurrence of a planned item with its resolved status.</summary>
public sealed record PlannedOccurrence(
    Guid PlannedItemId,
    string Description,
    decimal Amount,
    Guid? CategoryId,
    DateOnly DueDate,
    PlannedOccurrenceStatus Status,
    Guid? MatchedTransactionId
);

public enum PlannedOccurrenceStatus
{
    Pending,
    Matched,
    Overdue,
    Skipped,
}
