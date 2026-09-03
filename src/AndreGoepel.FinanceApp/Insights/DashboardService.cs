using AndreGoepel.FinanceApp.Domain.Budgets;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="IDashboardService"/> over the <see cref="TransactionView"/>
/// read model. Spending is rolled up to the top-level category so the breakdown
/// stays readable; unconverted rows (no EUR amount) are excluded from the sums.
/// </summary>
internal sealed class DashboardService(IQuerySession session) : IDashboardService
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
            .GroupBy(t => TopLevelName(t.CategoryId, categoriesById))
            .Select(g => new CategorySpend(g.Key, -g.Sum(t => t.AmountEur!.Value)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        var budgets = await BuildBudgetProgressAsync(
            start,
            expenseTransactions,
            categoriesById,
            cancellationToken
        );

        return new MonthlyOverview(
            income,
            expenses,
            income - expenses,
            spending,
            budgets,
            UnconvertedCount: transactions.Count(t => t.AmountEur is null),
            UncategorizedCount: transactions.Count(t => t.CategoryId is null)
        );
    }

    private async Task<IReadOnlyList<BudgetProgress>> BuildBudgetProgressAsync(
        DateOnly periodMonth,
        IReadOnlyList<TransactionView> expenseTransactions,
        IReadOnlyDictionary<Guid, Category> categoriesById,
        CancellationToken cancellationToken
    )
    {
        var budgets = await session.Query<Budget>().ToListAsync(cancellationToken);
        var active = budgets.Where(b => IsActive(b, periodMonth)).ToList();
        if (active.Count == 0)
        {
            return [];
        }

        var parents = categoriesById.Values.ToDictionary(c => c.Id, c => c.ParentId);
        var expenses = expenseTransactions
            .Select(t => (t.CategoryId, Amount: -t.AmountEur!.Value))
            .ToList();
        var limits = active
            .GroupBy(b => b.CategoryId)
            .ToDictionary(g => g.Key, g => g.First().MonthlyLimit);

        return BudgetCalculator
            .Compute(parents, expenses, limits)
            .Select(b => new BudgetProgress(NameOf(b.CategoryId, categoriesById), b.Limit, b.Spent))
            .OrderByDescending(b => b.Percent)
            .ToList();
    }

    /// <summary>Whether a budget period covers the given month (both bounds inclusive, open-ended when <see cref="Budget.EndMonth"/> is null).</summary>
    private static bool IsActive(Budget budget, DateOnly month) =>
        month >= budget.StartMonth && (budget.EndMonth is null || month <= budget.EndMonth);

    private static string NameOf(Guid categoryId, IReadOnlyDictionary<Guid, Category> byId) =>
        byId.TryGetValue(categoryId, out var category) ? category.Name : "Unknown";

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
