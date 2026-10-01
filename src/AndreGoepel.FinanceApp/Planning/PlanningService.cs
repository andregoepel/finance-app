using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using AndreGoepel.FinanceApp.Insights;
using Marten;

namespace AndreGoepel.FinanceApp.Planning;

/// <summary>
/// Implements <see cref="IPlanningService"/> by expanding active planned items into
/// occurrences and folding in <see cref="PlannedMatch"/> records: a matched
/// occurrence carries its transaction's actual amount; an unmatched past occurrence
/// is overdue; otherwise pending.
/// </summary>
internal sealed class PlanningService(IQuerySession session, INetWorthService netWorthService)
    : IPlanningService
{
    private const int CandidateWindowDays = 60;
    private const int UpcomingLimit = 10;

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
        var occurrences = await BuildOccurrencesAsync(from, to, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        decimal? estimatedEndOfMonthValue = null;
        if (year == today.Year && month == today.Month)
        {
            var netWorth = await netWorthService.GetAsync(cancellationToken: cancellationToken);
            estimatedEndOfMonthValue = EndOfMonthEstimator.Calculate(netWorth.Current, occurrences);
        }

        return new PlanMonth(
            occurrences,
            occurrences.Where(o => o.Amount > 0).Sum(o => o.Amount),
            -occurrences.Where(o => o.Amount < 0).Sum(o => o.Amount),
            occurrences.Where(o => o.MatchedAmount > 0).Sum(o => o.MatchedAmount!.Value),
            -occurrences.Where(o => o.MatchedAmount < 0).Sum(o => o.MatchedAmount!.Value),
            estimatedEndOfMonthValue
        );
    }

    public async Task<IReadOnlyList<PlannedOccurrence>> GetUpcomingAsync(
        int daysAhead = 30,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var occurrences = await BuildOccurrencesAsync(
            today.AddMonths(-2),
            today.AddDays(daysAhead),
            cancellationToken
        );
        return occurrences
            .Where(o => o.Status != PlannedOccurrenceStatus.Matched)
            .Take(UpcomingLimit)
            .ToList();
    }

    private async Task<IReadOnlyList<PlannedOccurrence>> BuildOccurrencesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken
    )
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var items = await session.Query<PlannedItem>().ToListAsync(cancellationToken);

        var due = items
            .Where(i => i.Active)
            .SelectMany(i =>
                PlannedOccurrenceExpander
                    .Expand(i.Schedule, from, to)
                    .Select(d => (Item: i, Date: d))
            )
            .ToList();

        // Load every link so allocations include occurrences in other months and inactive items.
        var matches = await session.Query<PlannedMatch>().ToListAsync(cancellationToken);
        var matchesByOccurrence = matches.ToLookup(m => (m.PlannedItemId, m.DueDate));

        var transactionIds = matches.Select(m => m.TransactionId).Distinct().ToArray();
        var txnById = (
            transactionIds.Length == 0
                ? []
                : await session
                    .Query<TransactionView>()
                    .Where(t => t.Id.IsOneOf(transactionIds))
                    .ToListAsync(cancellationToken)
        ).ToDictionary(t => t.Id);

        var itemById = items.ToDictionary(item => item.Id);
        var allocations = matches
            .GroupBy(match => match.TransactionId)
            .SelectMany(group =>
                PlannedAmountAllocator.Allocate(
                    txnById.TryGetValue(group.Key, out var transaction)
                        ? transaction.AmountEur
                        : null,
                    group,
                    itemById
                )
            )
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return due.Select(o =>
            {
                var lines = matchesByOccurrence[(o.Item.Id, o.Date)]
                    .Select(m => new MatchedTransaction(
                        m.TransactionId,
                        allocations.GetValueOrDefault(m.Id)
                    ))
                    .ToList();

                var status =
                    lines.Count > 0 ? PlannedOccurrenceStatus.Matched
                    : o.Date < today ? PlannedOccurrenceStatus.Overdue
                    : PlannedOccurrenceStatus.Pending;

                return new PlannedOccurrence(
                    o.Item.Id,
                    o.Item.Description,
                    o.Item.Amount,
                    o.Item.CategoryId,
                    o.Date,
                    status,
                    lines,
                    lines.Count == 0 ? null : lines.Sum(l => l.AmountEur ?? 0)
                );
            })
            .OrderBy(o => o.DueDate)
            .ThenBy(o => o.Description)
            .ToList();
    }

    public async Task<IReadOnlyList<TransactionView>> GetMatchCandidatesAsync(
        DateOnly dueDate,
        decimal plannedAmount,
        CancellationToken cancellationToken = default
    )
    {
        // Deliberately does not exclude transactions already matched elsewhere: a
        // single transaction can satisfy more than one occurrence (e.g. one
        // transfer covering rent and a car payment), so an already-matched
        // transaction is still a valid candidate here. The caller excludes
        // candidates already linked to the specific occurrence being matched.
        var from = dueDate.AddDays(-CandidateWindowDays);
        var to = dueDate.AddDays(CandidateWindowDays);
        var query = session
            .Query<TransactionView>()
            .Where(t =>
                t.TransferCounterpartId == null && t.BookingDate >= from && t.BookingDate <= to
            );
        query = plannedAmount > 0 ? query.Where(t => t.Amount > 0) : query.Where(t => t.Amount < 0);

        return (await query.ToListAsync(cancellationToken))
            .OrderBy(t => Math.Abs(t.BookingDate.DayNumber - dueDate.DayNumber))
            .Take(50)
            .ToList();
    }
}

internal static class EndOfMonthEstimator
{
    public static decimal Calculate(
        decimal currentValue,
        IReadOnlyList<PlannedOccurrence> occurrences
    ) =>
        currentValue
        + occurrences
            .Where(occurrence =>
                occurrence.Status
                    is PlannedOccurrenceStatus.Pending
                        or PlannedOccurrenceStatus.Overdue
            )
            .Sum(occurrence => occurrence.Amount);
}
