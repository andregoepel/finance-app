namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Pairs unlinked transactions that look like the two legs of a transfer
/// between own accounts: opposite signs, different accounts, amounts that
/// cancel out (in EUR, so cross-currency pairs still match), and booking
/// dates close together. Pure and side-effect-free — <see
/// cref="MatchTransfersCommandHandler"/> turns the result into events and
/// review-queue entries.
/// </summary>
public static class TransferMatcher
{
    /// <summary>Booking dates within this many days apart may be auto-linked outright.</summary>
    internal const int ExactMaxDayDifference = 1;

    /// <summary>Booking dates within this many days apart are offered for review.</summary>
    internal const int MaxDayDifference = 5;

    /// <summary>Relative amount tolerance for a review suggestion (of the larger EUR amount).</summary>
    internal const decimal RelativeTolerance = 0.01m;

    /// <summary>Absolute amount tolerance floor for a review suggestion, in EUR.</summary>
    internal const decimal AbsoluteToleranceEur = 2m;

    public static TransferMatchResult FindPairs(IReadOnlyList<TransferCandidate> candidates)
    {
        var pool = candidates.Where(c => c.AmountEur is not null).ToList();

        // Exact tier: every pairing that meets the exact criteria, kept only where
        // it is each leg's sole exact candidate — two same-day, same-amount
        // transfers competing for one counterpart must not be auto-linked at random.
        var exactCandidates = AllPairs(pool, PassKind.Exact);
        var exact = exactCandidates
            .Where(pair =>
                CountOf(exactCandidates, pair.OutgoingId) == 1
                && CountOf(exactCandidates, pair.IncomingId) == 1
            )
            .ToList();

        // Fuzzy tier: every remaining valid pairing is offered, deliberately
        // without the exact tier's uniqueness filter — when a transaction has two
        // plausible counterparts, the review queue is supposed to show both so a
        // person can pick the right one (AcceptTransferSuggestionCommand clears
        // the other suggestion once one is accepted).
        var linkedIds = exact.SelectMany(p => new[] { p.OutgoingId, p.IncomingId }).ToHashSet();
        var remaining = pool.Where(c => !linkedIds.Contains(c.Id)).ToList();
        var fuzzy = AllPairs(remaining, PassKind.Fuzzy)
            .OrderBy(p => p.DayDifference)
            .ThenBy(p => p.AmountDifferenceEur)
            .ToList();

        return new TransferMatchResult(exact, fuzzy);
    }

    private static int CountOf(IReadOnlyList<TransferPair> pairs, Guid transactionId) =>
        pairs.Count(p => p.OutgoingId == transactionId || p.IncomingId == transactionId);

    private static List<TransferPair> AllPairs(IReadOnlyList<TransferCandidate> pool, PassKind kind)
    {
        var pairs = new List<TransferPair>();
        for (var i = 0; i < pool.Count; i++)
        for (var j = i + 1; j < pool.Count; j++)
        {
            var pair = TryPair(pool[i], pool[j], kind);
            if (pair is not null)
            {
                pairs.Add(pair);
            }
        }
        return pairs;
    }

    private static TransferPair? TryPair(TransferCandidate a, TransferCandidate b, PassKind kind)
    {
        if (a.AccountId == b.AccountId)
        {
            return null;
        }

        var (outgoing, incoming) = a.AmountEur < 0 ? (a, b) : (b, a);
        if (outgoing.AmountEur >= 0 || incoming.AmountEur <= 0)
        {
            return null; // same sign
        }

        var dayDifference = Math.Abs(
            outgoing.BookingDate.DayNumber - incoming.BookingDate.DayNumber
        );
        var amountDifference = Math.Abs(outgoing.AmountEur!.Value + incoming.AmountEur!.Value);

        if (kind == PassKind.Exact)
        {
            var isExact =
                outgoing.Currency == incoming.Currency
                && amountDifference == 0m
                && dayDifference <= ExactMaxDayDifference;
            return isExact
                ? new TransferPair(outgoing.Id, incoming.Id, dayDifference, amountDifference)
                : null;
        }

        var tolerance = Math.Max(
            Math.Max(Math.Abs(outgoing.AmountEur.Value), incoming.AmountEur.Value)
                * RelativeTolerance,
            AbsoluteToleranceEur
        );
        var isFuzzy = dayDifference <= MaxDayDifference && amountDifference <= tolerance;
        return isFuzzy
            ? new TransferPair(outgoing.Id, incoming.Id, dayDifference, amountDifference)
            : null;
    }

    private enum PassKind
    {
        Exact,
        Fuzzy,
    }
}

/// <summary>A transaction offered up for transfer matching (EUR amount required).</summary>
public sealed record TransferCandidate(
    Guid Id,
    Guid AccountId,
    DateOnly BookingDate,
    string Currency,
    decimal? AmountEur
);

/// <summary>
/// A candidate pairing — <paramref name="OutgoingId"/> is the negative leg,
/// <paramref name="IncomingId"/> the positive one.
/// </summary>
public sealed record TransferPair(
    Guid OutgoingId,
    Guid IncomingId,
    int DayDifference,
    decimal AmountDifferenceEur
);

/// <summary><see cref="TransferMatcher.FindPairs"/>'s two confidence tiers.</summary>
public sealed record TransferMatchResult(
    IReadOnlyList<TransferPair> Exact,
    IReadOnlyList<TransferPair> Fuzzy
);
