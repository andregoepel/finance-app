using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Transactions;
using AndreGoepel.FinanceApp.Planning;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="IDashboardService"/> over the <see cref="TransactionView"/>
/// read model. Spending is rolled up to the top-level category so the breakdown
/// stays readable; unconverted rows (no EUR amount) are excluded from the sums.
/// </summary>
internal sealed class DashboardService(
    IQuerySession session,
    IMonthlyCategoryPlanService monthlyCategoryPlanService
) : IDashboardService
{
    public async Task<MonthlyOverview> GetMonthlyOverviewAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    )
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);

        // TransferCounterpartId (not the computed IsTransfer) so Marten can translate it.
        var transactions = await session
            .Query<TransactionView>()
            .Where(t =>
                t.BookingDate >= start && t.BookingDate < end && t.TransferCounterpartId == null
            )
            .ToListAsync(cancellationToken);

        var income = transactions.Where(t => t.AmountEur > 0).Sum(t => t.AmountEur!.Value);
        var expenses = -transactions.Where(t => t.AmountEur < 0).Sum(t => t.AmountEur!.Value);

        var categoriesById = (
            await session.Query<Category>().ToListAsync(cancellationToken)
        ).ToDictionary(c => c.Id);

        var expenseTransactions = transactions.Where(t => t.AmountEur < 0).ToList();

        var spending = expenseTransactions
            .SelectMany(t =>
                t.EffectiveCategoryLines.Select(line =>
                    (
                        Category: TopLevelName(line.CategoryId, categoriesById),
                        AmountEur: t.EurAmountFor(line)!.Value
                    )
                )
            )
            .GroupBy(x => x.Category)
            .Select(g => new CategorySpend(g.Key, -g.Sum(x => x.AmountEur)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        var budgets = (await monthlyCategoryPlanService.GetAsync(year, month, cancellationToken))
            .Select(plan => new BudgetProgress(
                plan.Category,
                plan.BudgetLimit,
                plan.ActualSpent,
                plan.PlannedRemaining
            ))
            .ToList();

        return new MonthlyOverview(
            income,
            expenses,
            income - expenses,
            spending,
            budgets,
            UnconvertedCount: transactions.Count(t => t.AmountEur is null),
            UncategorizedCount: transactions.Count(t => !t.IsCategorized)
        );
    }

    /// <summary>Walks a category up to its top-level ancestor; "Uncategorized" when unset/unknown.</summary>
    private static string TopLevelName(Guid? categoryId, IReadOnlyDictionary<Guid, Category> byId)
    {
        if (categoryId is not Guid id || !byId.TryGetValue(id, out var category))
        {
            return "Uncategorized";
        }
        while (category.ParentId is Guid parentId && byId.TryGetValue(parentId, out var parent))
        {
            category = parent;
        }
        return category.Name;
    }
}
