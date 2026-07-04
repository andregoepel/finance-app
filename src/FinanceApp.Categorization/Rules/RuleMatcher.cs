using FinanceApp.Domain.Categories;

namespace FinanceApp.Categorization.Rules;

/// <summary>
/// Deterministic rule matching: all set conditions must hold (case-insensitive
/// contains for text, inclusive bounds for amounts). The most specific matching
/// rule wins; ties go to the newest rule (later corrections refine earlier ones).
/// </summary>
public static class RuleMatcher
{
    public static CategoryRule? FindMatch(
        IReadOnlyList<CategoryRule> rules,
        string? counterparty,
        string description,
        decimal amount
    ) =>
        rules
            .Where(rule => Matches(rule, counterparty, description, amount))
            .OrderByDescending(rule => rule.Specificity)
            .ThenByDescending(rule => rule.CreatedAt)
            .FirstOrDefault();

    internal static bool Matches(
        CategoryRule rule,
        string? counterparty,
        string description,
        decimal amount
    )
    {
        // A rule without any text condition would match everything — ignore it.
        if (
            string.IsNullOrWhiteSpace(rule.CounterpartyContains)
            && string.IsNullOrWhiteSpace(rule.DescriptionContains)
        )
        {
            return false;
        }

        if (
            rule.CounterpartyContains is { } counterpartyPattern
            && (
                counterparty is null
                || !counterparty.Contains(counterpartyPattern, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return false;
        }

        if (
            rule.DescriptionContains is { } descriptionPattern
            && !description.Contains(descriptionPattern, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (rule.MinAmount is decimal min && amount < min)
        {
            return false;
        }
        if (rule.MaxAmount is decimal max && amount > max)
        {
            return false;
        }

        return true;
    }
}
