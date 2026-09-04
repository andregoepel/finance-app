using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Domain.NetWorth;

public static class NetWorthForecastCalculator
{
    public static IReadOnlyList<NetWorthPoint> Compute(
        decimal currentNetWorth,
        DateOnly today,
        IReadOnlyList<PlannedItem> plannedItems,
        IReadOnlySet<(Guid PlannedItemId, DateOnly DueDate)> matchedOccurrences,
        int months = 12
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);

        var monthEnds = Enumerable
            .Range(1, months)
            .Select(offset => today.AddMonths(offset))
            .Select(date => new DateOnly(
                date.Year,
                date.Month,
                DateTime.DaysInMonth(date.Year, date.Month)
            ))
            .ToList();
        var forecastEnd = monthEnds[^1];
        var occurrences = plannedItems
            .Where(item => item.Active)
            .SelectMany(item =>
                PlannedOccurrenceExpander
                    .Expand(item.Schedule, today, forecastEnd)
                    .Where(dueDate => !matchedOccurrences.Contains((item.Id, dueDate)))
                    .Select(dueDate => (Date: dueDate, item.Amount))
            )
            .OrderBy(occurrence => occurrence.Date)
            .ToList();

        var points = new List<NetWorthPoint>(months + 1) { new(today, currentNetWorth) };
        var forecast = currentNetWorth;
        var applied = 0;
        foreach (var monthEnd in monthEnds)
        {
            while (applied < occurrences.Count && occurrences[applied].Date <= monthEnd)
            {
                forecast += occurrences[applied].Amount;
                applied++;
            }
            points.Add(new NetWorthPoint(monthEnd, forecast));
        }

        return points;
    }
}
