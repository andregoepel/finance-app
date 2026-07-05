using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Planning;

/// <summary>
/// Read side for planning: the planned items and, per month, their expanded
/// occurrences (with match status) plus plan-vs-actual totals. Also supplies
/// candidate transactions for manual matching.
/// </summary>
public interface IPlanningService
{
    Task<IReadOnlyList<PlannedItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    Task<PlanMonth> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    );

    /// <summary>Unmatched transactions near a due date, offered for a manual match.</summary>
    Task<IReadOnlyList<TransactionView>> GetMatchCandidatesAsync(
        DateOnly dueDate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Unmatched occurrences that are overdue or due within <paramref name="daysAhead"/>
    /// days — for the dashboard's upcoming/overdue tile.
    /// </summary>
    Task<IReadOnlyList<PlannedOccurrence>> GetUpcomingAsync(
        int daysAhead = 30,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A month's occurrences with planned vs. actual (matched) totals (EUR).</summary>
public sealed record PlanMonth(
    IReadOnlyList<PlannedOccurrence> Occurrences,
    decimal PlannedIncome,
    decimal PlannedExpenses,
    decimal ActualIncome,
    decimal ActualExpenses
);
