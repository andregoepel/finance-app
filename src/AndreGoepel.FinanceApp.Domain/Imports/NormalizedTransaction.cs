namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// One canonical transaction row produced by a statement parser (or, in
/// Phase 3, an API connector) before deduplication. Amounts are always
/// <c>decimal</c>; <see cref="RawData"/> keeps the original provider row for
/// audit and troubleshooting.
/// </summary>
public sealed record NormalizedTransaction(
    int SourceRow,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    decimal Amount,
    string Currency,
    string? Counterparty,
    string Description,
    string? ExternalId,
    string RawData
);
