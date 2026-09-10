using AndreGoepel.FinanceApp.Domain.Budgets;

namespace AndreGoepel.FinanceApp.Domain.NetWorth;

/// <summary>
/// Calculates the expense still expected in one calendar month. Planned expenses
/// are absorbed by their nearest budget envelope and therefore are never counted twice.
/// </summary>
public static class BudgetForecastCalculator
{
    public static decimal RemainingExpense(
        IReadOnlyDictionary<Guid, Guid?> categoryParents,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> actualExpenses,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> plannedExpenses,
        IReadOnlyDictionary<Guid, decimal> budgetLimits
    ) =>
        MonthlyCategoryPlanCalculator
            .Compute(categoryParents, actualExpenses, plannedExpenses, budgetLimits)
            .Sum(plan => plan.ForecastRemaining);
}
