namespace AndreGoepel.FinanceApp.Domain.Categories;

/// <summary>
/// Backfill counterpart of <c>CategorizeImportedTransactionsCommand</c>: runs the
/// categorization pipeline (rules first, Claude fallback) over every transaction
/// that is still uncategorized, regardless of which import brought it in.
/// Transfer legs and transactions already waiting in the review queue with a
/// suggestion are skipped. Published fire-and-forget from the review page;
/// kept in the Domain assembly for the same reason as its sibling.
/// </summary>
public sealed record CategorizeUncategorizedTransactionsCommand;
