namespace FinanceApp.Domain.Transactions;

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

public enum CategorySource
{
    Provider,
    Rule,
    Ai,
    Manual,
}
