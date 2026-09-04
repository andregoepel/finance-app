using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

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
        ILogger<MatchPlannedTransactionsCommand> logger,
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

        // Auto-match only fills genuinely open occurrences — one match, from
        // either auto or a prior manual pick, is enough to take an occurrence
        // out of consideration here. Splitting across several transactions stays
        // a deliberate manual action (see SetPlannedMatchCommand).
        var existing = await session.Query<PlannedMatch>().ToListAsync(cancellationToken);
        var matchedOccurrences = existing.Select(m => (m.PlannedItemId, m.DueDate)).ToHashSet();

        // Precompute the candidate window bounds — Marten can't translate
        // `from.AddDays(-window)` inline (a captured-variable negation/method call).
        var window = items.Max(i => i.DateWindowDays);
        var candidateFrom = from.AddDays(-window);
        var candidateTo = to.AddDays(window);
        var pool = (
            await session
                .Query<TransactionView>()
                .Where(t =>
                    t.AmountEur != null
                    && !t.IsPlanMatched
                    && t.BookingDate >= candidateFrom
                    && t.BookingDate <= candidateTo
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
                if (matchedOccurrences.Contains((item.Id, due)))
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
                        Id = PlannedMatch.KeyFor(item.Id, due, transactionId),
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
                if (stream.Aggregate is { IsPlanMatched: false })
                {
                    stream.AppendOne(new TransactionMatchedToPlannedItem(item.Id, due));
                }

                pool.RemoveAll(c => c.Id == transactionId);
                matchedOccurrences.Add((item.Id, due));
                matchedAny = true;
            }
        }

        if (matchedAny)
        {
            try
            {
                await session.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrentUpdateException)
            {
                // This command is published after every account sync, so a burst of
                // syncs (e.g. "Sync All") can have two runs match the same transaction
                // and race to append it. Whichever loses is skipped rather than
                // failing the whole message — the next publish re-matches cleanly.
                logger.LogInformation(
                    "Skipped a batch of planned-item matches: a concurrent run already applied them."
                );
            }
        }
    }
}
