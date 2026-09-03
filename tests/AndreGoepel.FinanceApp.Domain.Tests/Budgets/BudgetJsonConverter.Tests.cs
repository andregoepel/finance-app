using System.Text.Json;
using AndreGoepel.FinanceApp.Domain.Budgets;

namespace AndreGoepel.FinanceApp.Domain.Tests.Budgets;

/// <summary>
/// Proves rows written before <see cref="Budget.CategoryId"/>/<see cref="Budget.StartMonth"/>
/// existed still deserialize — the exact shape stored for every budget before this feature,
/// and the crash reported after deploying it (missing required properties).
/// </summary>
public sealed class BudgetJsonConverterTests
{
    [Fact]
    public void Deserialize_LegacyShape_FallsBackToDocumentIdAsCategoryId()
    {
        // Arrange — the only shape ever written before CategoryId/StartMonth existed.
        var id = Guid.NewGuid();
        var json = $$"""{"Id":"{{id}}","MonthlyLimit":200}""";

        // Act
        var budget = JsonSerializer.Deserialize<Budget>(json)!;

        // Assert
        Assert.Equal(id, budget.Id);
        Assert.Equal(id, budget.CategoryId);
        Assert.Equal(200m, budget.MonthlyLimit);
        Assert.Equal(DateOnly.MinValue, budget.StartMonth);
        Assert.Null(budget.EndMonth);
    }

    [Fact]
    public void Deserialize_LegacyShape_IsCaseInsensitive()
    {
        // Arrange — in case the JSON casing convention differs from the C# member names.
        var id = Guid.NewGuid();
        var json = $$"""{"id":"{{id}}","monthlyLimit":150}""";

        // Act
        var budget = JsonSerializer.Deserialize<Budget>(json)!;

        // Assert
        Assert.Equal(id, budget.CategoryId);
        Assert.Equal(150m, budget.MonthlyLimit);
    }

    [Fact]
    public void RoundTrip_CurrentShape_PreservesEveryField()
    {
        // Arrange
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            MonthlyLimit = 425.5m,
            StartMonth = new DateOnly(2026, 3, 1),
            EndMonth = new DateOnly(2026, 9, 1),
        };

        // Act
        var json = JsonSerializer.Serialize(budget);
        var roundTripped = JsonSerializer.Deserialize<Budget>(json)!;

        // Assert
        Assert.Equal(budget.Id, roundTripped.Id);
        Assert.Equal(budget.CategoryId, roundTripped.CategoryId);
        Assert.Equal(budget.MonthlyLimit, roundTripped.MonthlyLimit);
        Assert.Equal(budget.StartMonth, roundTripped.StartMonth);
        Assert.Equal(budget.EndMonth, roundTripped.EndMonth);
    }

    [Fact]
    public void RoundTrip_OpenEndedBudget_KeepsEndMonthNull()
    {
        // Arrange
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            MonthlyLimit = 100m,
            StartMonth = new DateOnly(2026, 1, 1),
            EndMonth = null,
        };

        // Act
        var roundTripped = JsonSerializer.Deserialize<Budget>(JsonSerializer.Serialize(budget))!;

        // Assert
        Assert.Null(roundTripped.EndMonth);
    }
}
