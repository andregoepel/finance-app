namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// A transaction entered the system via CSV upload or API sync. Carries the
/// normalized fields plus the dedup hash and the raw provider data for audit.
/// Money is <c>decimal</c> only; <see cref="AmountEur"/> is set when the
/// original currency is EUR and back-filled via ECB rates in Phase 4 otherwise.
/// <see cref="OriginalAmount"/>/<see cref="OriginalCurrency"/> carry the
/// pre-conversion foreign leg when the provider reports one in a currency
/// different from the booked <see cref="Currency"/>.
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
    string? RawData,
    decimal? OriginalAmount = null,
    string? OriginalCurrency = null
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

/// <summary>One category's share of a split transaction's amount.</summary>
public sealed record CategoryLine(Guid CategoryId, decimal Amount);

/// <summary>
/// Splits a transaction across two or more categories, each with its own share
/// of the amount (shares sum to the transaction's total amount). Replaces
/// whatever single-category or prior split state existed — never an in-place
/// update. Not currently produced by rules/history/AI; a manual action only.
/// </summary>
public sealed record TransactionCategorySplit(
    IReadOnlyList<CategoryLine> Lines,
    CategorySource Source
);

/// <summary>
/// Marks this transaction as one leg of a transfer between own accounts —
/// excluded from spending aggregations.
/// </summary>
public sealed record TransactionLinkedAsTransfer(Guid CounterpartTransactionId);

/// <summary>Reverts an erroneous transfer link.</summary>
public sealed record TransactionTransferUnlinked;

/// <summary>One planned occurrence a transaction has been matched to.</summary>
public sealed record PlannedLink(Guid PlannedItemId, DateOnly DueDate);

/// <summary>
/// Links this transaction to a planned item's occurrence (auto or manual match)
/// — the actual that satisfies a planned cost/income. A transaction may satisfy
/// more than one occurrence (e.g. one transfer covering rent and a car
/// payment) and an occurrence may be satisfied by more than one transaction
/// (e.g. a salary paid out in two bookings) — each such pairing appends its own
/// event; applying the same pairing twice is a no-op. The <c>PlannedMatch</c>
/// document (one per pairing) is the source of truth for plan-vs-actual; this
/// event lets the transactions grid show the link.
/// </summary>
public sealed record TransactionMatchedToPlannedItem(Guid PlannedItemId, DateOnly DueDate);

/// <summary>
/// Clears one planned-item match from this transaction. <paramref name="PlannedItemId"/>
/// and <paramref name="DueDate"/> are optional only so that events recorded
/// before a transaction could hold more than one link — where the occurrence
/// was implicit, there being just the one — still deserialize; going forward
/// they are always set and identify exactly which link to drop.
/// </summary>
public sealed record TransactionPlannedMatchCleared(
    Guid? PlannedItemId = null,
    DateOnly? DueDate = null
);

public enum CategorySource
{
    Provider,
    Rule,
    Ai,
    Manual,

    /// <summary>
    /// Applied because the household had already confirmed this counterparty by
    /// hand, consistently, at least twice. Appended last: Marten stores enums as
    /// integers, so existing values must keep their position.
    /// </summary>
    History,
}
