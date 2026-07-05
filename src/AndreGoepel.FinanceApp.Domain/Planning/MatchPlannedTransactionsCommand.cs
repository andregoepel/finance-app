using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Auto-matches planned occurrences (over a recent window) to unmatched
/// transactions. Idempotent — already-matched occurrences and already-used
/// transactions are skipped, so it is safe to publish after every import and on a
/// schedule, and to invoke on demand from the Planning page.
/// </summary>
public sealed record MatchPlannedTransactionsCommand;

public static class MatchPlannedTransactionsCommandHandler
{
    private const int WindowMonthsBack = 6;

    public static async Task Handle(
        MatchPlannedTransactionsCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var from = monthStart.AddMonths(-WindowMonthsBack);
        var to = monthStart.AddMonths(1).AddDays(-1);

        var items = await session
            .Query<PlannedItem>()
            .Where(i => i.Active)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        var existing = await session.Query<PlannedMatch>().ToListAsync(cancellationToken);
        var matchedKeys = existing.Select(m => m.Id).ToHashSet();

        var window = items.Max(i => i.DateWindowDays);
        var pool = (
            await session
                .Query<TransactionView>()
                .Where(t =>
                    t.AmountEur != null
                    && t.PlannedItemId == null
                    && t.BookingDate >= from.AddDays(-window)
                    && t.BookingDate <= to.AddDays(window)
                )
                .ToListAsync(cancellationToken)
        )
            .Select(t => new MatchCandidate(
                t.Id,
                t.AccountId,
                t.BookingDate,
                t.AmountEur!.Value,
                t.Counterparty,
                t.Description
            ))
            .ToList();

        var matchedAny = false;
        foreach (var item in items)
        {
            foreach (var due in PlannedOccurrenceExpander.Expand(item.Schedule, from, to))
            {
                var key = PlannedMatch.KeyFor(item.Id, due);
                if (matchedKeys.Contains(key))
                {
                    continue;
                }

                var criteria = new PlannedMatchCriteria(
                    item.Amount,
                    due,
                    item.AmountTolerance,
                    item.DateWindowDays,
                    item.CounterpartyPattern,
                    item.ExpectedAccountId
                );
                if (PlannedMatcher.FindMatch(criteria, pool) is not Guid transactionId)
                {
                    continue;
                }

                session.Store(
                    new PlannedMatch
                    {
                        Id = key,
                        PlannedItemId = item.Id,
                        DueDate = due,
                        TransactionId = transactionId,
                        Auto = true,
                    }
                );
                var stream = await session.Events.FetchForWriting<TransactionView>(
                    transactionId,
                    cancellationToken
                );
                if (stream.Aggregate is { PlannedItemId: null })
                {
                    stream.AppendOne(new TransactionMatchedToPlannedItem(item.Id, due));
                }

                pool.RemoveAll(c => c.Id == transactionId);
                matchedKeys.Add(key);
                matchedAny = true;
            }
        }

        if (matchedAny)
        {
            await session.SaveChangesAsync(cancellationToken);
        }
    }
}
