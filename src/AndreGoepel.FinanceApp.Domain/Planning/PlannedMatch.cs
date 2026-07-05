namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// Records that a transaction satisfies a specific planned occurrence — the source
/// of truth for plan-vs-actual. One per occurrence; the id encodes the planned
/// item and its due date so a re-match is idempotent.
/// </summary>
public sealed class PlannedMatch
{
    /// <summary>Document id: <c>{plannedItemId}:{dueDate:yyyy-MM-dd}</c>.</summary>
    public required string Id { get; init; }

    public required Guid PlannedItemId { get; init; }

    public required DateOnly DueDate { get; init; }

    public required Guid TransactionId { get; set; }

    /// <summary>True when matched automatically; false when set manually.</summary>
    public bool Auto { get; set; }

    public DateTimeOffset MatchedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string KeyFor(Guid plannedItemId, DateOnly dueDate) =>
        $"{plannedItemId}:{dueDate:yyyy-MM-dd}";
}
