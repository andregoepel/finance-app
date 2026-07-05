namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Picks the transaction that best satisfies a planned occurrence: same sign,
/// amount within tolerance, booking date within the window, and (when set) the
/// counterparty pattern and expected account. The closest by date then by amount
/// wins. Pure and side-effect-free.
/// </summary>
public static class PlannedMatcher
{
    public static Guid? FindMatch(
        PlannedMatchCriteria criteria,
        IReadOnlyList<MatchCandidate> candidates
    )
    {
        var minDate = criteria.DueDate.AddDays(-criteria.WindowDays);
        var maxDate = criteria.DueDate.AddDays(criteria.WindowDays);
        var tolerance = Math.Abs(criteria.Amount) * criteria.Tolerance;

        return candidates
            .Where(c =>
                c.BookingDate >= minDate
                && c.BookingDate <= maxDate
                && Math.Sign(c.AmountEur) == Math.Sign(criteria.Amount)
                && Math.Abs(c.AmountEur - criteria.Amount) <= tolerance
                && (criteria.ExpectedAccountId is null || c.AccountId == criteria.ExpectedAccountId)
                && (
                    string.IsNullOrWhiteSpace(criteria.CounterpartyPattern)
                    || Contains(c, criteria.CounterpartyPattern)
                )
            )
            .OrderBy(c => Math.Abs(c.BookingDate.DayNumber - criteria.DueDate.DayNumber))
            .ThenBy(c => Math.Abs(c.AmountEur - criteria.Amount))
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();
    }

    private static bool Contains(MatchCandidate candidate, string pattern) =>
        (candidate.Counterparty?.Contains(pattern, StringComparison.OrdinalIgnoreCase) ?? false)
        || candidate.Description.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a planned occurrence looks for in a transaction.</summary>
public sealed record PlannedMatchCriteria(
    decimal Amount,
    DateOnly DueDate,
    decimal Tolerance,
    int WindowDays,
    string? CounterpartyPattern,
    Guid? ExpectedAccountId
);

/// <summary>A transaction offered up for matching (EUR amount).</summary>
public sealed record MatchCandidate(
    Guid Id,
    Guid AccountId,
    DateOnly BookingDate,
    decimal AmountEur,
    string? Counterparty,
    string Description
);
