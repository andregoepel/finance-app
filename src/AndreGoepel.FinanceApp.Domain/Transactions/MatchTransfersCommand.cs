using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Auto-matches unlinked transactions across accounts as transfers: a
/// same-currency pair (exact EUR amount, opposite sign, different account,
/// same booking date) is linked outright; a cross-currency exact match or an
/// ambiguous duplicate becomes a <see cref="TransferSuggestion"/> for the
/// review queue instead. No tolerance on date or amount — see
/// <see cref="TransferMatcher"/>. A transaction the household has manually
/// categorized (<see cref="CategorySource.Manual"/>) is excluded from the
/// candidate pool outright — that is a standing decision that it is not a
/// transfer, and must survive even if the <see cref="TransferSuggestion"/>
/// collection itself is ever cleared out. Every run also sweeps existing
/// pending suggestions and dismisses any whose leg was categorized by hand
/// in the meantime (see <see cref="FindStaleSuggestionsAsync"/>), so a
/// suggestion never outlives the decision the household already made about
/// it. Idempotent — already linked transactions and already-suggested pairs
/// are skipped, so it is safe to publish after every import/sync and to
/// invoke on demand from the review page, mirroring
/// <c>MatchPlannedTransactionsCommand</c>.
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

        var stale = await FindStaleSuggestionsAsync(session, pending, cancellationToken);
        foreach (var suggestion in stale)
        {
            suggestion.Dismissed = true;
            session.Store(suggestion);
        }
        var stillPending = pending.Except(stale).ToList();

        var awaitingReview = stillPending
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
                .Where(t =>
                    t.AmountEur != null
                    && t.TransferCounterpartId == null
                    && (t.CategorySource == null || t.CategorySource != CategorySource.Manual)
                )
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

        var linked = 0;
        var suggested = 0;
        if (pool.Count >= 2)
        {
            var result = TransferMatcher.FindPairs(pool);

            foreach (var pair in result.Exact)
            {
                if (await TryLinkAsync(session, pair, cancellationToken))
                {
                    linked++;
                }
            }

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
                    }
                );
                suggested++;
            }
        }

        if (linked == 0 && suggested == 0 && stale.Count == 0)
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
            "Transfer matching: {Linked} pairs auto-linked, {Suggested} pairs queued for review, {Stale} stale suggestions dismissed, {Pool} candidates considered.",
            linked,
            suggested,
            stale.Count,
            pool.Count
        );
    }

    /// <summary>
    /// Pending suggestions whose leg was categorized by hand since the suggestion
    /// was created — e.g. the household categorized it directly on the
    /// Transactions page instead of reviewing the suggestion. These are dismissed
    /// outright rather than left to clutter the review queue for a decision that
    /// has already effectively been made.
    /// </summary>
    private static async Task<List<TransferSuggestion>> FindStaleSuggestionsAsync(
        IDocumentSession session,
        IReadOnlyList<TransferSuggestion> pending,
        CancellationToken cancellationToken
    )
    {
        if (pending.Count == 0)
        {
            return [];
        }

        var legIds = pending
            .SelectMany(s => new[] { s.OutgoingTransactionId, s.IncomingTransactionId })
            .Distinct()
            .ToArray();
        var manuallyCategorizedLegIds = (
            await session
                .Query<TransactionView>()
                .Where(t => t.Id.IsOneOf(legIds) && t.CategorySource == CategorySource.Manual)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        return pending
            .Where(s =>
                manuallyCategorizedLegIds.Contains(s.OutgoingTransactionId)
                || manuallyCategorizedLegIds.Contains(s.IncomingTransactionId)
            )
            .ToList();
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
