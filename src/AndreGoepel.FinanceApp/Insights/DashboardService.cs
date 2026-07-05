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

        var spending = transactions
            .Where(t => t.AmountEur < 0)
            .GroupBy(t => TopLevelName(t.CategoryId, categoriesById))
            .Select(g => new CategorySpend(g.Key, -g.Sum(t => t.AmountEur!.Value)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        return new MonthlyOverview(
            income,
            expenses,
            income - expenses,
            spending,
            UnconvertedCount: transactions.Count(t => t.AmountEur is null),
            UncategorizedCount: transactions.Count(t => t.CategoryId is null)
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
