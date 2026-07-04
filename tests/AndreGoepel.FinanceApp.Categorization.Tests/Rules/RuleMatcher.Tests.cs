using AndreGoepel.FinanceApp.Categorization.Rules;
using AndreGoepel.FinanceApp.Domain.Categories;

namespace AndreGoepel.FinanceApp.Categorization.Tests.Rules;

public class RuleMatcherTests
{
    private static CategoryRule Rule(
        string? counterparty = null,
        string? description = null,
        decimal? min = null,
        decimal? max = null,
        Guid? categoryId = null,
        DateTimeOffset? createdAt = null
    ) =>
        new()
        {
            CategoryId = categoryId ?? Guid.NewGuid(),
            CounterpartyContains = counterparty,
            DescriptionContains = description,
            MinAmount = min,
            MaxAmount = max,
            Source = CategoryRuleSource.Manual,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    [Fact]
    public void Matches_CounterpartyContains_IsCaseInsensitive()
    {
        // Arrange
        var rule = Rule(counterparty: "rewe");

        // Act + Assert
        Assert.True(RuleMatcher.Matches(rule, "REWE Markt GmbH", "any", -10m));
        Assert.False(RuleMatcher.Matches(rule, "Lidl", "any", -10m));
        Assert.False(RuleMatcher.Matches(rule, null, "any", -10m));
    }

    [Fact]
    public void Matches_AmountBounds_AreInclusive()
    {
        // Arrange
        var rule = Rule(description: "abo", min: -50m, max: -10m);

        // Act + Assert
        Assert.True(RuleMatcher.Matches(rule, null, "Abo Monat", -10m));
        Assert.True(RuleMatcher.Matches(rule, null, "Abo Monat", -50m));
        Assert.False(RuleMatcher.Matches(rule, null, "Abo Monat", -9.99m));
        Assert.False(RuleMatcher.Matches(rule, null, "Abo Monat", -50.01m));
    }

    [Fact]
    public void Matches_RuleWithoutTextCondition_NeverMatches()
    {
        // Arrange
        var rule = Rule(min: -100m, max: 0m);

        // Act + Assert
        Assert.False(RuleMatcher.Matches(rule, "anything", "anything", -10m));
    }

    [Fact]
    public void FindMatch_MoreSpecificRuleWins()
    {
        // Arrange
        var broad = Rule(counterparty: "amazon");
        var specific = Rule(counterparty: "amazon", description: "prime", min: -20m);

        // Act
        var match = RuleMatcher.FindMatch(
            [broad, specific],
            "AMAZON EU",
            "Amazon Prime Abo",
            -8.99m
        );

        // Assert
        Assert.Same(specific, match);
    }

    [Fact]
    public void FindMatch_TieBreaksOnNewestRule()
    {
        // Arrange
        var older = Rule(counterparty: "spotify", createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var newer = Rule(counterparty: "spotify", createdAt: DateTimeOffset.UtcNow);

        // Act
        var match = RuleMatcher.FindMatch([older, newer], "Spotify AB", "Premium", -9.99m);

        // Assert
        Assert.Same(newer, match);
    }

    [Fact]
    public void FindMatch_NoMatchingRule_ReturnsNull()
    {
        // Act + Assert
        Assert.Null(RuleMatcher.FindMatch([Rule(counterparty: "rewe")], "Lidl", "Einkauf", -5m));
    }
}
