namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// A transfer pairing that matches exactly (same day, same EUR amount) but
/// could not be auto-linked with full confidence — either a cross-currency
/// pair, whose FX leg always needs a human glance, or one of several
/// candidates competing for the same counterpart. One per unordered pair of
/// transactions; <see cref="Id"/> encodes both so a re-match is idempotent.
/// </summary>
public sealed class TransferSuggestion
{
    public required string Id { get; init; }

    public required Guid OutgoingTransactionId { get; init; }

    public required Guid IncomingTransactionId { get; init; }

    /// <summary>
    /// True once dismissed as "not a transfer" — kept rather than deleted so the
    /// matcher, which re-runs over unchanged history after every import, does not
    /// silently resurrect it.
    /// </summary>
    public bool Dismissed { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static string KeyFor(Guid first, Guid second) =>
        first.CompareTo(second) <= 0 ? $"{first}:{second}" : $"{second}:{first}";
}
