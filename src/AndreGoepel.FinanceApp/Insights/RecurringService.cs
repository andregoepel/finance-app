using AndreGoepel.FinanceApp.Domain.Recurring;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="IRecurringService"/>: pulls the recent transaction
/// history (EUR, non-transfer) and runs the pure <see cref="RecurringDetector"/>.
/// Transactions without a counterparty fall back to their description as the key.
/// </summary>
internal sealed class RecurringService(IQuerySession session) : IRecurringService
{
    public async Task<IReadOnlyList<RecurringSeries>> GetAsync(
        int monthsBack = 13,
        CancellationToken cancellationToken = default
    )
    {
        var since = DateOnly.FromDateTime(DateTime.Today).AddMonths(-monthsBack);

        var transactions = await session
            .Query<TransactionView>()
            .Where(t =>
                t.BookingDate >= since && t.AmountEur != null && t.TransferCounterpartId == null
            )
            .ToListAsync(cancellationToken);

        var candidates = transactions
            .Select(t => new RecurringCandidate(
                string.IsNullOrWhiteSpace(t.Counterparty) ? t.Description : t.Counterparty,
                t.BookingDate,
                t.AmountEur!.Value
            ))
            .Where(c => !string.IsNullOrWhiteSpace(c.Counterparty))
            .ToList();

        return RecurringDetector.Detect(candidates);
    }
}
