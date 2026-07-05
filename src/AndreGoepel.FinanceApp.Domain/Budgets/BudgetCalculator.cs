namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>
/// Rolls monthly expenses up against budgets. A budget on a category counts
/// spending in that category and every descendant, so an expense contributes to
/// the budget of any of its ancestors (and itself). Pure and side-effect-free.
/// </summary>
public static class BudgetCalculator
{
    /// <param name="categoryParents">Each category id mapped to its parent id (<c>null</c> for roots).</param>
    /// <param name="expenses">Expense transactions this month: category (nullable) and positive EUR amount.</param>
    /// <param name="budgetLimits">Budgeted category id → monthly limit.</param>
    public static IReadOnlyList<BudgetSpend> Compute(
        IReadOnlyDictionary<Guid, Guid?> categoryParents,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> expenses,
        IReadOnlyDictionary<Guid, decimal> budgetLimits
    )
    {
        var spent = budgetLimits.Keys.ToDictionary(id => id, _ => 0m);

        foreach (var (categoryId, amount) in expenses)
        {
            if (categoryId is not Guid id)
            {
                continue; // uncategorized spend counts toward no budget
            }

            Guid? current = id;
            var guard = 0;
            while (current is Guid node && guard++ < 64)
            {
                if (spent.ContainsKey(node))
                {
                    spent[node] += amount;
                }
                current = categoryParents.TryGetValue(node, out var parent) ? parent : null;
            }
        }

        return budgetLimits.Select(b => new BudgetSpend(b.Key, b.Value, spent[b.Key])).ToList();
    }
}

/// <summary>A budget's monthly limit and the spending measured against it (EUR).</summary>
public sealed record BudgetSpend(Guid CategoryId, decimal Limit, decimal Spent);
