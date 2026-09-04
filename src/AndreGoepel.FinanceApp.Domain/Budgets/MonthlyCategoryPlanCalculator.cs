namespace AndreGoepel.FinanceApp.Domain.Budgets;

public static class MonthlyCategoryPlanCalculator
{
    public static IReadOnlyList<MonthlyCategoryPlanTotal> Compute(
        IReadOnlyDictionary<Guid, Guid?> categoryParents,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> actualExpenses,
        IReadOnlyList<(Guid? CategoryId, decimal Amount)> plannedExpenses,
        IReadOnlyDictionary<Guid, decimal> budgetLimits
    )
    {
        var plannedTargets = plannedExpenses
            .Select(expense =>
                FindBudgetAncestor(expense.CategoryId, categoryParents, budgetLimits)
            )
            .ToList();
        var targetIds = budgetLimits.Keys.ToHashSet();
        foreach (var target in plannedTargets)
        {
            targetIds.Add(target);
        }

        var actualByTarget = targetIds.ToDictionary(id => id, _ => 0m);
        foreach (var expense in actualExpenses)
        {
            var target =
                expense.CategoryId is Guid categoryId
                    ? FindNearest(categoryId, targetIds.Contains, categoryParents)
                : targetIds.Contains(Guid.Empty) ? Guid.Empty
                : null;
            if (target is Guid targetId)
            {
                actualByTarget[targetId] += expense.Amount;
            }
        }

        var plannedByTarget = targetIds.ToDictionary(id => id, _ => 0m);
        for (var index = 0; index < plannedExpenses.Count; index++)
        {
            plannedByTarget[plannedTargets[index]] += plannedExpenses[index].Amount;
        }

        return targetIds
            .Select(categoryId => new MonthlyCategoryPlanTotal(
                categoryId == Guid.Empty ? null : categoryId,
                budgetLimits.TryGetValue(categoryId, out var limit) ? limit : null,
                actualByTarget[categoryId],
                plannedByTarget[categoryId]
            ))
            .ToList();
    }

    private static Guid FindBudgetAncestor(
        Guid? categoryId,
        IReadOnlyDictionary<Guid, Guid?> parents,
        IReadOnlyDictionary<Guid, decimal> budgets
    ) =>
        FindNearest(categoryId, id => budgets.ContainsKey(id), parents) ?? categoryId ?? Guid.Empty;

    private static Guid? FindNearest(
        Guid? categoryId,
        Func<Guid, bool> matches,
        IReadOnlyDictionary<Guid, Guid?> parents
    )
    {
        var current = categoryId;
        for (var guard = 0; current is Guid id && guard < 64; guard++)
        {
            if (matches(id))
            {
                return id;
            }
            current = parents.TryGetValue(id, out var parent) ? parent : null;
        }
        return null;
    }
}

public sealed record MonthlyCategoryPlanTotal(
    Guid? CategoryId,
    decimal? BudgetLimit,
    decimal ActualSpent,
    decimal PlannedRemaining
)
{
    public decimal ForecastSpent => ActualSpent + PlannedRemaining;

    public decimal? FlexibleRemaining => BudgetLimit - ForecastSpent;
}
