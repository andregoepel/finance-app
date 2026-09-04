using AndreGoepel.FinanceApp.Domain.Budgets;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Planning;

internal sealed class MonthlyCategoryPlanService(IQuerySession session)
    : IMonthlyCategoryPlanService
{
    public async Task<IReadOnlyList<MonthlyCategoryPlan>> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    )
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var categories = await session.Query<Category>().ToListAsync(cancellationToken);
        var categoryById = categories.ToDictionary(category => category.Id);
        var parents = categories.ToDictionary(
            category => category.Id,
            category => category.ParentId
        );
        var budgets = await session.Query<Budget>().ToListAsync(cancellationToken);
        var limits = budgets
            .Where(budget =>
                start >= budget.StartMonth && (budget.EndMonth is null || start <= budget.EndMonth)
            )
            .ToDictionary(budget => budget.CategoryId, budget => budget.MonthlyLimit);

        var transactions = await session
            .Query<TransactionView>()
            .Where(transaction =>
                transaction.BookingDate >= start
                && transaction.BookingDate < end
                && transaction.TransferCounterpartId == null
                && transaction.AmountEur < 0
            )
            .ToListAsync(cancellationToken);
        var actual = transactions
            .SelectMany(transaction =>
                transaction.EffectiveCategoryLines.Select(line =>
                    ((Guid?)line.CategoryId, Amount: -transaction.EurAmountFor(line)!.Value)
                )
            )
            .ToList();

        var items = await session
            .Query<PlannedItem>()
            .Where(item => item.Active && item.Amount < 0)
            .ToListAsync(cancellationToken);
        var due = items
            .SelectMany(item =>
                PlannedOccurrenceExpander
                    .Expand(item.Schedule, start, end.AddDays(-1))
                    .Select(date => (Item: item, Date: date))
            )
            .ToList();
        var itemIds = due.Select(occurrence => occurrence.Item.Id).Distinct().ToArray();
        var matches =
            itemIds.Length == 0
                ? []
                : await session
                    .Query<PlannedMatch>()
                    .Where(match => match.PlannedItemId.IsOneOf(itemIds))
                    .ToListAsync(cancellationToken);
        var matched = matches.Select(match => (match.PlannedItemId, match.DueDate)).ToHashSet();
        var planned = due.Where(occurrence =>
                !matched.Contains((occurrence.Item.Id, occurrence.Date))
            )
            .Select(occurrence => ((Guid?)occurrence.Item.CategoryId, -occurrence.Item.Amount))
            .ToList();

        return MonthlyCategoryPlanCalculator
            .Compute(parents, actual, planned, limits)
            .Select(total => new MonthlyCategoryPlan(
                total.CategoryId,
                total.CategoryId is Guid id && categoryById.TryGetValue(id, out var category)
                    ? category.Name
                    : null,
                total.BudgetLimit,
                total.ActualSpent,
                total.PlannedRemaining
            ))
            .OrderBy(row => row.Category is null)
            .ThenBy(row => row.Category)
            .ToList();
    }
}
