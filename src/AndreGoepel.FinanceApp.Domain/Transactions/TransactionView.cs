namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Flat single-stream projection of the event-sourced transaction — the write
/// model for command handlers (via <c>FetchForWriting</c>) and the read model
/// for the transactions grid. Projected inline so dedup checks see imports from
/// the same session.
/// </summary>
public sealed class TransactionView
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public DateOnly BookingDate { get; set; }

    public DateOnly? ValueDate { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "";

    public decimal? AmountEur { get; set; }

    public decimal? OriginalAmount { get; set; }

    public string? OriginalCurrency { get; set; }

    public string? Counterparty { get; set; }

    public string Description { get; set; } = "";

    public string? ExternalId { get; set; }

    public string DedupHash { get; set; } = "";

    public Guid ImportBatchId { get; set; }

    public Guid? CategoryId { get; set; }

    public CategorySource? CategorySource { get; set; }

    public decimal? CategoryConfidence { get; set; }

    public Guid? TransferCounterpartId { get; set; }

    public bool IsTransfer => TransferCounterpartId is not null;

    /// <summary>
    /// The planned occurrences this transaction is matched to — usually zero or
    /// one, more than one only when a single transaction was deliberately split
    /// across planned items (e.g. one transfer covering rent and a car payment).
    /// </summary>
    public IReadOnlyList<PlannedLink> PlannedLinks { get; set; } = [];

    /// <summary>
    /// True once <see cref="PlannedLinks"/> is non-empty. A real stored field
    /// (not computed from <see cref="PlannedLinks"/>) so Marten can translate it
    /// into SQL for the auto-matcher's candidate query — the same reason
    /// <see cref="IsTransfer"/> stays unused in queries in favor of
    /// <see cref="TransferCounterpartId"/>.
    /// </summary>
    public bool IsPlanMatched { get; set; }

    public static TransactionView Create(TransactionImported imported) =>
        new()
        {
            Id = imported.TransactionId,
            AccountId = imported.AccountId,
            BookingDate = imported.BookingDate,
            ValueDate = imported.ValueDate,
            Amount = imported.Amount,
            Currency = imported.Currency,
            AmountEur = imported.AmountEur,
            OriginalAmount = imported.OriginalAmount,
            OriginalCurrency = imported.OriginalCurrency,
            Counterparty = imported.Counterparty,
            Description = imported.Description,
            ExternalId = imported.ExternalId,
            DedupHash = imported.DedupHash,
            ImportBatchId = imported.ImportBatchId,
        };

    public void Apply(TransactionEurAmountAssigned converted)
    {
        AmountEur = converted.AmountEur;
    }

    public void Apply(TransactionCategorized categorized)
    {
        CategoryId = categorized.CategoryId;
        CategorySource = categorized.Source;
        CategoryConfidence = categorized.Confidence;
    }

    public void Apply(TransactionCategoryCorrected corrected)
    {
        CategoryId = corrected.CategoryId;
        CategorySource = Transactions.CategorySource.Manual;
        CategoryConfidence = null;
    }

    public void Apply(TransactionLinkedAsTransfer linked)
    {
        TransferCounterpartId = linked.CounterpartTransactionId;
    }

    public void Apply(TransactionTransferUnlinked _)
    {
        TransferCounterpartId = null;
    }

    public void Apply(TransactionMatchedToPlannedItem matched)
    {
        if (
            PlannedLinks.Any(l =>
                l.PlannedItemId == matched.PlannedItemId && l.DueDate == matched.DueDate
            )
        )
        {
            return; // already linked to this occurrence — idempotent
        }
        PlannedLinks = [.. PlannedLinks, new PlannedLink(matched.PlannedItemId, matched.DueDate)];
        IsPlanMatched = true;
    }

    public void Apply(TransactionPlannedMatchCleared cleared)
    {
        PlannedLinks =
            cleared.PlannedItemId is Guid plannedItemId && cleared.DueDate is DateOnly dueDate
                ? PlannedLinks
                    .Where(l => l.PlannedItemId != plannedItemId || l.DueDate != dueDate)
                    .ToList()
                : []; // event recorded before multi-match existed — there was only ever one link
        IsPlanMatched = PlannedLinks.Count > 0;
    }
}
