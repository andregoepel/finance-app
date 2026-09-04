using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Recurring;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="IRecurringService"/>: pulls the recent transaction
/// history (EUR, non-transfer) and runs the pure <see cref="RecurringDetector"/>.
/// Transactions without a counterparty fall back to their description as the key.
/// Marks the series that already have a planned item, which the detector cannot
/// know about — that is a planning fact, not a property of the transactions.
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

        var detected = RecurringDetector.Detect(candidates);
        var alreadyPlanned = await AlreadyPlannedKeysAsync(cancellationToken);
        var dismissed = await DismissedKeysAsync(cancellationToken);
        return
        [
            .. detected
                .Where(s => !dismissed.Contains(s.Counterparty))
                .Select(s => s with { AlreadyPlanned = alreadyPlanned.Contains(s.Counterparty) }),
        ];
    }

    /// <summary>
    /// Series keys that an active planned item was created from. Only active items
    /// count: retiring a planned item is how you say the series is not planned any
    /// more, and the page should offer to add it again rather than keep claiming
    /// it is covered.
    /// </summary>
    private async Task<HashSet<string>> AlreadyPlannedKeysAsync(CancellationToken cancellationToken)
    {
        var keys = await session
            .Query<PlannedItem>()
            .Where(i => i.Active && i.CreatedFromRecurringKey != null)
            .Select(i => i.CreatedFromRecurringKey!)
            .ToListAsync(cancellationToken);
        return [.. keys];
    }

    /// <summary>Series keys the household has flagged as a false positive — see <see cref="DismissedRecurringSeries"/>.</summary>
    private async Task<HashSet<string>> DismissedKeysAsync(CancellationToken cancellationToken)
    {
        var keys = await session
            .Query<DismissedRecurringSeries>()
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        return [.. keys];
    }
}
