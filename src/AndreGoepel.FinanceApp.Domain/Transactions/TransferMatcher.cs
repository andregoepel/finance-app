namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Pairs unlinked transactions that look like the two legs of a transfer
/// between own accounts: opposite signs, different accounts, the same booking
/// date, and an amount that cancels out exactly (in EUR, so cross-currency
/// pairs still match). No tolerance on either date or amount — a fuzzy window
/// produced too many false positives in practice. Pure and side-effect-free —
/// <see cref="MatchTransfersCommandHandler"/> turns the result into events and
/// review-queue entries.
/// </summary>
public static class TransferMatcher
{
    public static TransferMatchResult FindPairs(IReadOnlyList<TransferCandidate> candidates)
    {
        var pool = candidates.Where(c => c.AmountEur is not null).ToList();

        // A same-currency pair auto-links only when it is each leg's sole exact
        // candidate — two same-day, same-amount transfers competing for one
        // counterpart must not be auto-linked at random.
        var allPairs = AllPairs(pool);
        var exact = allPairs
            .Where(pair =>
                pair.SameCurrency
                && CountOf(allPairs, pair.OutgoingId) == 1
                && CountOf(allPairs, pair.IncomingId) == 1
            )
            .ToList();

        // Review tier: whatever is left once the auto-linked legs are removed —
        // a cross-currency match (the EUR amounts agree, but the FX leg always
        // needs a human glance) or an ambiguous same-currency duplicate.
        var linkedIds = exact.SelectMany(p => new[] { p.OutgoingId, p.IncomingId }).ToHashSet();
        var remaining = pool.Where(c => !linkedIds.Contains(c.Id)).ToList();
        var fuzzy = AllPairs(remaining);

        return new TransferMatchResult(exact, fuzzy);
    }

    private static int CountOf(IReadOnlyList<TransferPair> pairs, Guid transactionId) =>
        pairs.Count(p => p.OutgoingId == transactionId || p.IncomingId == transactionId);

    private static List<TransferPair> AllPairs(IReadOnlyList<TransferCandidate> pool)
    {
        var pairs = new List<TransferPair>();
        for (var i = 0; i < pool.Count; i++)
        for (var j = i + 1; j < pool.Count; j++)
        {
            var pair = TryPair(pool[i], pool[j]);
            if (pair is not null)
            {
                pairs.Add(pair);
            }
        }
        return pairs;
    }

    private static TransferPair? TryPair(TransferCandidate a, TransferCandidate b)
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

        if (outgoing.BookingDate != incoming.BookingDate)
        {
            return null;
        }

        if (outgoing.AmountEur!.Value + incoming.AmountEur!.Value != 0m)
        {
            return null;
        }

        return new TransferPair(outgoing.Id, incoming.Id, outgoing.Currency == incoming.Currency);
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
/// <paramref name="IncomingId"/> the positive one. <paramref name="SameCurrency"/>
/// is false for a cross-currency match, which never auto-links.
/// </summary>
public sealed record TransferPair(Guid OutgoingId, Guid IncomingId, bool SameCurrency);

/// <summary><see cref="TransferMatcher.FindPairs"/>'s two confidence tiers.</summary>
public sealed record TransferMatchResult(
    IReadOnlyList<TransferPair> Exact,
    IReadOnlyList<TransferPair> Fuzzy
);
