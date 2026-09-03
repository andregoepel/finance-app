using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Auto-matches unlinked transactions across accounts as transfers: an exact
/// pair (same EUR amount, opposite sign, different account, booking dates a
/// day apart at most) is linked outright; anything less certain becomes a
/// <see cref="TransferSuggestion"/> for the review queue. Idempotent — already
/// linked transactions and already-suggested pairs are skipped, so it is safe
/// to publish after every import/sync and to invoke on demand from the review
/// page, mirroring <c>MatchPlannedTransactionsCommand</c>.
/// </summary>
public sealed record MatchTransfersCommand;

public static class MatchTransfersCommandHandler
{
    public static async Task Handle(
        MatchTransfersCommand command,
        IDocumentSession session,
        ILogger<MatchTransfersCommand> logger,
        CancellationToken cancellationToken
    )
    {
        var pending = await session
            .Query<TransferSuggestion>()
            .Where(s => !s.Dismissed)
            .ToListAsync(cancellationToken);
        var awaitingReview = pending
            .SelectMany(s => new[] { s.OutgoingTransactionId, s.IncomingTransactionId })
            .ToHashSet();
        var suggestedPairIds = (
            await session
                .Query<TransferSuggestion>()
                .Select(s => s.Id)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        var pool = (
            await session
                .Query<TransactionView>()
                .Where(t => t.AmountEur != null && t.TransferCounterpartId == null)
                .ToListAsync(cancellationToken)
        )
            .Where(t => !awaitingReview.Contains(t.Id))
            .Select(t => new TransferCandidate(
                t.Id,
                t.AccountId,
                t.BookingDate,
                t.Currency,
                t.AmountEur
            ))
            .ToList();
        if (pool.Count < 2)
        {
            return;
        }

        var result = TransferMatcher.FindPairs(pool);

        var linked = 0;
        foreach (var pair in result.Exact)
        {
            if (await TryLinkAsync(session, pair, cancellationToken))
            {
                linked++;
            }
        }

        var suggested = 0;
        foreach (var pair in result.Fuzzy)
        {
            var key = TransferSuggestion.KeyFor(pair.OutgoingId, pair.IncomingId);
            if (!suggestedPairIds.Add(key))
            {
                continue; // already pending or previously dismissed
            }

            session.Store(
                new TransferSuggestion
                {
                    Id = key,
                    OutgoingTransactionId = pair.OutgoingId,
                    IncomingTransactionId = pair.IncomingId,
                    DayDifference = pair.DayDifference,
                    AmountDifferenceEur = pair.AmountDifferenceEur,
                }
            );
            suggested++;
        }

        if (linked == 0 && suggested == 0)
        {
            return;
        }

        try
        {
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentUpdateException)
        {
            // Published after every sync, so a burst of syncs can have two runs match the
            // same transaction and race to append it. Whichever loses is skipped rather than
            // failing the whole message — the next publish re-matches cleanly.
            logger.LogInformation(
                "Skipped a batch of transfer matches: a concurrent run already applied them."
            );
            return;
        }

        logger.LogInformation(
            "Transfer matching: {Linked} pairs auto-linked, {Suggested} pairs queued for review, {Pool} candidates considered.",
            linked,
            suggested,
            pool.Count
        );
    }

    private static async Task<bool> TryLinkAsync(
        IDocumentSession session,
        TransferPair pair,
        CancellationToken cancellationToken
    )
    {
        var first = await session.Events.FetchForWriting<TransactionView>(
            pair.OutgoingId,
            cancellationToken
        );
        var second = await session.Events.FetchForWriting<TransactionView>(
            pair.IncomingId,
            cancellationToken
        );
        if (
            first.Aggregate is not { IsTransfer: false }
            || second.Aggregate is not { IsTransfer: false }
        )
        {
            return false; // linked or removed by a concurrent run since the pool was loaded
        }

        first.AppendOne(new TransactionLinkedAsTransfer(pair.IncomingId));
        second.AppendOne(new TransactionLinkedAsTransfer(pair.OutgoingId));
        return true;
    }
}
