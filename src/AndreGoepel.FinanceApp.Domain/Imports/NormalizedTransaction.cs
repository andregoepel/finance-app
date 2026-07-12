namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// One canonical transaction row produced by a statement parser (or, in
/// Phase 3, an API connector) before deduplication. Amounts are always
/// <c>decimal</c>; <see cref="RawData"/> keeps the original provider row for
/// audit and troubleshooting. <see cref="OriginalAmount"/> and
/// <see cref="OriginalCurrency"/> are set only when the provider reports an
/// original amount in a currency different from the booked one;
/// <see cref="Amount"/>/<see cref="Currency"/> remain the balance impact.
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
    string RawData,
    decimal? OriginalAmount = null,
    string? OriginalCurrency = null
);
