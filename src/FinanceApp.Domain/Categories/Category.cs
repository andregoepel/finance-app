namespace FinanceApp.Domain.Categories;

/// <summary>Hierarchical spending/income category (e.g. Living &gt; Groceries).</summary>
public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>Parent category; <c>null</c> for top-level categories.</summary>
    public Guid? ParentId { get; set; }

    public int SortOrder { get; set; }
}
