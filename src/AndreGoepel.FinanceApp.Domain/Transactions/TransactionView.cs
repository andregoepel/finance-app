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

    /// <summary>The planned item this transaction is matched to, if any.</summary>
    public Guid? PlannedItemId { get; set; }

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
        PlannedItemId = matched.PlannedItemId;
    }

    public void Apply(TransactionPlannedMatchCleared _)
    {
        PlannedItemId = null;
    }
}
