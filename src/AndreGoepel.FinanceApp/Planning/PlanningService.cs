using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Planning;

/// <summary>
/// Implements <see cref="IPlanningService"/> by expanding active planned items into
/// a month's occurrences and folding in <see cref="PlannedMatch"/> records: a
/// matched occurrence carries its transaction's actual amount; an unmatched past
/// occurrence is overdue; otherwise pending.
/// </summary>
internal sealed class PlanningService(IQuerySession session) : IPlanningService
{
    private const int CandidateWindowDays = 45;

    public async Task<IReadOnlyList<PlannedItem>> GetItemsAsync(
        CancellationToken cancellationToken = default
    ) => await session.Query<PlannedItem>().ToListAsync(cancellationToken);

    public async Task<PlanMonth> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    )
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var items = await session
            .Query<PlannedItem>()
            .Where(i => i.Active)
            .ToListAsync(cancellationToken);

        var due = items
            .SelectMany(i =>
                PlannedOccurrenceExpander
                    .Expand(i.Schedule, from, to)
                    .Select(d => (Item: i, Date: d))
            )
            .ToList();

        var keys = due.Select(o => PlannedMatch.KeyFor(o.Item.Id, o.Date)).ToArray();
        var matches =
            keys.Length == 0
                ? []
                : (
                    await session
                        .Query<PlannedMatch>()
                        .Where(m => m.Id.IsOneOf(keys))
                        .ToListAsync(cancellationToken)
                );
        var matchByKey = matches.ToDictionary(m => m.Id);

        var transactionIds = matches.Select(m => m.TransactionId).Distinct().ToArray();
        var transactions =
            transactionIds.Length == 0
                ? []
                : (
                    await session
                        .Query<TransactionView>()
                        .Where(t => t.Id.IsOneOf(transactionIds))
                        .ToListAsync(cancellationToken)
                );
        var txnById = transactions.ToDictionary(t => t.Id);

        var occurrences = new List<PlannedOccurrence>();
        foreach (var (item, date) in due)
        {
            matchByKey.TryGetValue(PlannedMatch.KeyFor(item.Id, date), out var match);
            var matchedTxn =
                match is not null && txnById.TryGetValue(match.TransactionId, out var t) ? t : null;

            var status =
                match is not null ? PlannedOccurrenceStatus.Matched
                : date < today ? PlannedOccurrenceStatus.Overdue
                : PlannedOccurrenceStatus.Pending;

            occurrences.Add(
                new PlannedOccurrence(
                    item.Id,
                    item.Description,
                    item.Amount,
                    item.CategoryId,
                    date,
                    status,
                    match?.TransactionId,
                    matchedTxn?.AmountEur
                )
            );
        }

        occurrences = occurrences.OrderBy(o => o.DueDate).ThenBy(o => o.Description).ToList();
        return new PlanMonth(
            occurrences,
            occurrences.Where(o => o.Amount > 0).Sum(o => o.Amount),
            -occurrences.Where(o => o.Amount < 0).Sum(o => o.Amount),
            occurrences.Where(o => o.MatchedAmount > 0).Sum(o => o.MatchedAmount!.Value),
            -occurrences.Where(o => o.MatchedAmount < 0).Sum(o => o.MatchedAmount!.Value)
        );
    }

    public async Task<IReadOnlyList<TransactionView>> GetMatchCandidatesAsync(
        DateOnly dueDate,
        CancellationToken cancellationToken = default
    )
    {
        var from = dueDate.AddDays(-CandidateWindowDays);
        var to = dueDate.AddDays(CandidateWindowDays);
        return (
            await session
                .Query<TransactionView>()
                .Where(t =>
                    t.PlannedItemId == null
                    && t.TransferCounterpartId == null
                    && t.BookingDate >= from
                    && t.BookingDate <= to
                )
                .ToListAsync(cancellationToken)
        )
            .OrderBy(t => Math.Abs(t.BookingDate.DayNumber - dueDate.DayNumber))
            .Take(50)
            .ToList();
    }
}
