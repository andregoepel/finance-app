using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Planning;

/// <summary>
/// Read side for planning: the planned items and, per month, their expanded
/// occurrences with plan-vs-actual totals.
/// </summary>
public interface IPlanningService
{
    Task<IReadOnlyList<PlannedItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    Task<PlanMonth> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A month's planned occurrences and the planned income/expense totals (EUR).</summary>
public sealed record PlanMonth(
    IReadOnlyList<PlannedOccurrence> Occurrences,
    decimal PlannedIncome,
    decimal PlannedExpenses
);
