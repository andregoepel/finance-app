namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// A transaction entered the system via CSV upload or API sync. Carries the
/// normalized fields plus the dedup hash and the raw provider data for audit.
/// Money is <c>decimal</c> only; <see cref="AmountEur"/> is set when the
/// original currency is EUR and back-filled via ECB rates in Phase 4 otherwise.
/// </summary>
public sealed record TransactionImported(
    Guid TransactionId,
    Guid AccountId,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    decimal Amount,
    string Currency,
    decimal? AmountEur,
    string? Counterparty,
    string Description,
    string? ExternalId,
    string DedupHash,
    Guid ImportBatchId,
    string? RawData
);

/// <summary>
/// The EUR value of a non-EUR transaction, converted at the ECB reference rate
/// for the booking date. <see cref="RateDate"/> is the actual rate date used
/// (the nearest prior business day when the booking date is a weekend/holiday).
/// </summary>
public sealed record TransactionEurAmountAssigned(
    decimal AmountEur,
    decimal EurPerUnit,
    DateOnly RateDate
);

/// <summary>First category assignment. Corrections are separate events.</summary>
public sealed record TransactionCategorized(
    Guid CategoryId,
    CategorySource Source,
    decimal? Confidence
);

/// <summary>
/// Manual correction of an existing category — never an in-place update.
/// Corrections feed rule learning (Phase 2).
/// </summary>
public sealed record TransactionCategoryCorrected(Guid? PreviousCategoryId, Guid CategoryId);

/// <summary>
/// Marks this transaction as one leg of a transfer between own accounts —
/// excluded from spending aggregations.
/// </summary>
public sealed record TransactionLinkedAsTransfer(Guid CounterpartTransactionId);

/// <summary>Reverts an erroneous transfer link.</summary>
public sealed record TransactionTransferUnlinked;

/// <summary>
/// Links this transaction to a planned item's occurrence (auto or manual match)
/// — the actual that satisfies a planned cost/income. The
/// <c>PlannedMatch</c> document is the source of truth for plan-vs-actual; this
/// event lets the transactions grid show the link.
/// </summary>
public sealed record TransactionMatchedToPlannedItem(Guid PlannedItemId, DateOnly DueDate);

/// <summary>Clears a planned-item match from this transaction.</summary>
public sealed record TransactionPlannedMatchCleared;

public enum CategorySource
{
    Provider,
    Rule,
    Ai,
    Manual,
}
