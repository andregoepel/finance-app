using AndreGoepel.FinanceApp.Domain.Budgets;
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
    ) =>
        Compute(
            currentNetWorth,
            today,
            plannedItems,
            matchedOccurrences,
            new Dictionary<Guid, Guid?>(),
            [],
            [],
            months
        );

    public static IReadOnlyList<NetWorthPoint> Compute(
        decimal currentNetWorth,
        DateOnly today,
        IReadOnlyList<PlannedItem> plannedItems,
        IReadOnlySet<(Guid PlannedItemId, DateOnly DueDate)> matchedOccurrences,
        IReadOnlyDictionary<Guid, Guid?> categoryParents,
        IReadOnlyList<Budget> budgets,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> currentMonthActualExpenses,
        int months = 12
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);

        var currentMonthEnd = new DateOnly(
            today.Year,
            today.Month,
            DateTime.DaysInMonth(today.Year, today.Month)
        );
        var firstOffset = today == currentMonthEnd ? 1 : 0;
        var monthEnds = Enumerable
            .Range(firstOffset, months)
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
                    .Select(dueDate => (Date: dueDate, Item: item))
            )
            .ToList();

        var points = new List<NetWorthPoint>(months + 1) { new(today, currentNetWorth) };
        var forecast = currentNetWorth;
        foreach (var monthEnd in monthEnds)
        {
            var monthStart = new DateOnly(monthEnd.Year, monthEnd.Month, 1);
            var monthOccurrences = occurrences
                .Where(occurrence => occurrence.Date >= monthStart && occurrence.Date <= monthEnd)
                .ToList();
            var income = monthOccurrences
                .Where(occurrence => occurrence.Item.Amount > 0)
                .Sum(occurrence => occurrence.Item.Amount);
            var plannedExpenses = monthOccurrences
                .Where(occurrence => occurrence.Item.Amount < 0)
                .Select(occurrence => ((Guid?)occurrence.Item.CategoryId, -occurrence.Item.Amount))
                .ToList();
            var limits = budgets
                .Where(budget =>
                    monthStart >= budget.StartMonth
                    && (budget.EndMonth is null || monthStart <= budget.EndMonth)
                )
                .ToDictionary(budget => budget.CategoryId, budget => budget.MonthlyLimit);
            var actual =
                monthStart.Year == today.Year && monthStart.Month == today.Month
                    ? currentMonthActualExpenses
                    : [];
            var expense = BudgetForecastCalculator.RemainingExpense(
                categoryParents,
                actual,
                plannedExpenses,
                limits
            );

            forecast += income - expense;
            points.Add(new NetWorthPoint(monthEnd, forecast));
        }

        return points;
    }
}
