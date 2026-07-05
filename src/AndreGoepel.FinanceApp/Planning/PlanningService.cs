using AndreGoepel.FinanceApp.Domain.Planning;
using Marten;

namespace AndreGoepel.FinanceApp.Planning;

/// <summary>
/// Implements <see cref="IPlanningService"/> by expanding active planned items
/// into a month's occurrences. Matching against actual transactions arrives in a
/// later slice; for now an occurrence is overdue when its due date is past and
/// pending otherwise.
/// </summary>
internal sealed class PlanningService(IQuerySession session) : IPlanningService
{
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

        var occurrences = new List<PlannedOccurrence>();
        foreach (var item in items)
        {
            foreach (var due in PlannedOccurrenceExpander.Expand(item.Schedule, from, to))
            {
                var status =
                    due < today ? PlannedOccurrenceStatus.Overdue : PlannedOccurrenceStatus.Pending;
                occurrences.Add(
                    new PlannedOccurrence(
                        item.Id,
                        item.Description,
                        item.Amount,
                        item.CategoryId,
                        due,
                        status,
                        MatchedTransactionId: null
                    )
                );
            }
        }

        occurrences = occurrences.OrderBy(o => o.DueDate).ThenBy(o => o.Description).ToList();
        return new PlanMonth(
            occurrences,
            occurrences.Where(o => o.Amount > 0).Sum(o => o.Amount),
            -occurrences.Where(o => o.Amount < 0).Sum(o => o.Amount)
        );
    }
}
