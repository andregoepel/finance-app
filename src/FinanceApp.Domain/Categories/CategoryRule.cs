namespace FinanceApp.Domain.Categories;

/// <summary>
/// Deterministic categorization rule: all set conditions must match
/// (case-insensitive contains for the text patterns, inclusive bounds for the
/// amount range). Rules win over AI suggestions and cost nothing per import.
/// </summary>
public sealed class CategoryRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CategoryId { get; set; }

    public string? CounterpartyContains { get; set; }

    public string? DescriptionContains { get; set; }

    public decimal? MinAmount { get; set; }

    public decimal? MaxAmount { get; set; }

    public required CategoryRuleSource Source { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Number of set conditions — more specific rules win on conflicts.</summary>
    public int Specificity =>
        (string.IsNullOrWhiteSpace(CounterpartyContains) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(DescriptionContains) ? 0 : 1)
        + (MinAmount is null ? 0 : 1)
        + (MaxAmount is null ? 0 : 1);
}

public enum CategoryRuleSource
{
    Manual,
    LearnedFromCorrection,
}
