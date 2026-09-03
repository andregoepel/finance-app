namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Records that a transaction satisfies a specific planned occurrence — the source
/// of truth for plan-vs-actual. One per (occurrence, transaction) pairing, so an
/// occurrence can be satisfied by more than one transaction (e.g. a salary paid
/// out in two bookings) and a transaction can satisfy more than one occurrence
/// (e.g. one transfer covering rent and a car payment). The id encodes all three
/// parts so the same pairing is idempotent to re-match.
/// </summary>
public sealed class PlannedMatch
{
    /// <summary>Document id: <c>{plannedItemId}:{dueDate:yyyy-MM-dd}:{transactionId}</c>.</summary>
    public required string Id { get; init; }

    public required Guid PlannedItemId { get; init; }

    public required DateOnly DueDate { get; init; }

    public required Guid TransactionId { get; set; }

    /// <summary>True when matched automatically; false when set manually.</summary>
    public bool Auto { get; set; }

    public DateTimeOffset MatchedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string KeyFor(Guid plannedItemId, DateOnly dueDate, Guid transactionId) =>
        $"{plannedItemId}:{dueDate:yyyy-MM-dd}:{transactionId}";
}
