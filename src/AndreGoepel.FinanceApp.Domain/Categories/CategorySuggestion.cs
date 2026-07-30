namespace AndreGoepel.FinanceApp.Domain.Categories;

/// <summary>
/// A low-confidence AI category suggestion awaiting human review. One per
/// transaction (the transaction id is the document identity); deleted when the
/// suggestion is accepted, dismissed, or the transaction gets categorized any
/// other way. High-confidence suggestions are applied directly as events and
/// never stored here.
/// </summary>
/// <remarks>
/// Kept in the Domain assembly — like <c>CategorizeImportedTransactionsCommand</c>
/// — so account and transaction cleanup can purge the review queue without the
/// Domain referencing the categorization module. The suggestion commands and the
/// Claude pipeline that write it stay in <c>AndreGoepel.FinanceApp.Categorization</c>.
/// </remarks>
public sealed class CategorySuggestion
{
    /// <summary>Transaction id.</summary>
    public required Guid Id { get; init; }

    public required Guid CategoryId { get; init; }

    /// <summary>Model confidence in [0, 1].</summary>
    public required decimal Confidence { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
