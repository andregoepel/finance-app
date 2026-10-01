using AndreGoepel.FinanceApp.Domain.Accounts;
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
    public async Task<IReadOnlyList<MonthlyAccountOption>> GetMonthlyAccountOptionsAsync(
        CancellationToken cancellationToken = default
    ) =>
        (
            await session
                .Query<Account>()
                .Where(account => account.Status == AccountStatus.Active)
                .ToListAsync(cancellationToken)
        )
            .OrderBy(account => account.Name)
            .Select(account => new MonthlyAccountOption(
                account.Id,
                account.Name,
                account.IncludeInMonthlyOverviewByDefault
            ))
            .ToList();

    public async Task<MonthlyOverview> GetMonthlyOverviewAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? accountIds = null
    )
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);

        var actuals = await MonthlyActuals.LoadAsync(
            session,
            start,
            end,
            accountIds,
            cancellationToken
        );
        var transactions = actuals.Transactions;
        var income = actuals
            .Lines.Where(line => line.AmountEur > 0)
            .Sum(line => line.AmountEur!.Value);
        var expenses = -actuals
            .Lines.Where(line => line.AmountEur < 0)
            .Sum(line => line.AmountEur!.Value);

        var categoriesById = (
            await session.Query<Category>().ToListAsync(cancellationToken)
        ).ToDictionary(c => c.Id);

        var spending = actuals
            .Lines.Where(line => line.AmountEur < 0)
            .Select(line =>
                (
                    Category: TopLevelName(line.CategoryId, categoriesById),
                    AmountEur: line.AmountEur!.Value
                )
            )
            .GroupBy(x => x.Category)
            .Select(g => new CategorySpend(g.Key, -g.Sum(x => x.AmountEur)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        var budgets = (
            await monthlyCategoryPlanService.GetAsync(year, month, cancellationToken, accountIds)
        )
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
