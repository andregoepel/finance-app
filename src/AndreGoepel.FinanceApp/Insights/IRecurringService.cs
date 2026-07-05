using AndreGoepel.FinanceApp.Domain.Recurring;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Detects recurring payments and income from the transaction history (excluding
/// transfers), using EUR amounts and the counterparty as the grouping key.
/// </summary>
public interface IRecurringService
{
    Task<IReadOnlyList<RecurringSeries>> GetAsync(
        int monthsBack = 13,
        CancellationToken cancellationToken = default
    );
}
