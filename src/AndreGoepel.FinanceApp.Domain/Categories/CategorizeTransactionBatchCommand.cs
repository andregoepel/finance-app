namespace AndreGoepel.FinanceApp.Domain.Categories;

/// <summary>
/// One Claude round trip: at most a batch's worth of transactions that neither a
/// rule nor the household's history could categorize. Cascaded from the import
/// follow-up and the backfill so every message execution stays well inside
/// Wolverine's execution timeout — a backfill over thousands of rows becomes
/// dozens of these, processed one after another. <paramref name="Scope"/> only
/// names the originating run in the log.
/// </summary>
public sealed record CategorizeTransactionBatchCommand(
    IReadOnlyList<Guid> TransactionIds,
    string Scope
);
