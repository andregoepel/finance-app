namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// Published after a successful import; handled asynchronously by the
/// categorization pipeline (rules first, Claude fallback). Kept in the Domain
/// assembly so the import handler can publish it without referencing the
/// categorization module. Categorization failures never fail imports.
/// </summary>
public sealed record CategorizeImportedTransactionsCommand(Guid ImportBatchId);
