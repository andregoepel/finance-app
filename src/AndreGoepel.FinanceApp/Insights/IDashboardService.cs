namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Read-side aggregates for the dashboard. All amounts are in EUR and exclude
/// transfers between own accounts; transactions still awaiting an EUR amount are
/// left out of the sums and surfaced as <see cref="MonthlyOverview.UnconvertedCount"/>.
/// </summary>
public interface IDashboardService
{
    Task<MonthlyOverview> GetMonthlyOverviewAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Income/expense totals and spending breakdown for one month (EUR).</summary>
public sealed record MonthlyOverview(
    decimal Income,
    decimal Expenses,
    decimal Net,
    IReadOnlyList<CategorySpend> SpendingByCategory,
    IReadOnlyList<BudgetProgress> Budgets,
    int UnconvertedCount,
    int UncategorizedCount
);

/// <summary>Expense total for a top-level category in a month (positive EUR).</summary>
public sealed record CategorySpend(string Category, decimal Amount);

/// <summary>A budgeted category's limit and month-to-date spend (EUR).</summary>
public sealed record BudgetProgress(string Category, decimal Limit, decimal Spent)
{
    public decimal Percent => Limit <= 0 ? 0 : Math.Round(Spent / Limit * 100, 0);

    public bool IsOver => Spent > Limit;
}
