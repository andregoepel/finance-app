namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>
/// A monthly spending limit for a category (in EUR). One budget per category —
/// the document id is the category id. A budget on a parent category is measured
/// against spending in that category and all its descendants.
/// </summary>
public sealed class Budget
{
    /// <summary>Document identity — equals the category id this budget applies to.</summary>
    public required Guid Id { get; init; }

    public required decimal MonthlyLimit { get; set; }
}
