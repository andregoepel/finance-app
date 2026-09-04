using System.Text.Json.Serialization;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>
/// A monthly spending limit for a category (in EUR), valid for a range of months.
/// A category can have several budgets over time as the limit changes — only one
/// should be active for any given month, which <see cref="Budgets.SetBudgetCommand"/>
/// enforces by rejecting overlapping periods. A budget on a parent category is
/// measured against spending in that category and all its descendants.
/// </summary>
/// <remarks>
/// <see cref="BudgetJsonConverter"/> lets rows written before <see cref="CategoryId"/>/
/// <see cref="StartMonth"/> existed keep deserializing — see its doc comment.
/// </remarks>
[JsonConverter(typeof(BudgetJsonConverter))]
public sealed class Budget
{
    /// <summary>Document identity — one row per budget period, not per category.</summary>
    public required Guid Id { get; init; }

    public required Guid CategoryId { get; set; }

    public required decimal MonthlyLimit { get; set; }

    /// <summary>First month (day is always 1) this budget applies to.</summary>
    public required DateOnly StartMonth { get; set; }

    /// <summary>Last month (inclusive, day is always 1) this budget applies to; <c>null</c> means ongoing.</summary>
    public DateOnly? EndMonth { get; set; }
}
