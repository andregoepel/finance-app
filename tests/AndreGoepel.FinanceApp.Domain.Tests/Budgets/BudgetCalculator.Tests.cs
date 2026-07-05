using AndreGoepel.FinanceApp.Domain.Budgets;

namespace AndreGoepel.FinanceApp.Domain.Tests.Budgets;

public class BudgetCalculatorTests
{
    private static readonly Guid Living = Guid.NewGuid();
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly Guid Fun = Guid.NewGuid();

    private static readonly Dictionary<Guid, Guid?> Tree = new()
    {
        [Living] = null,
        [Groceries] = Living,
        [Rent] = Living,
        [Fun] = null,
    };

    [Fact]
    public void Compute_ParentBudget_IncludesDescendantSpending()
    {
        // Arrange — a €500 budget on Living should catch Groceries + Rent.
        var expenses = new List<(Guid?, decimal)>
        {
            (Groceries, 100m),
            (Rent, 300m),
            (Fun, 40m),
            (null, 25m), // uncategorized — counts toward nothing
        };
        var limits = new Dictionary<Guid, decimal> { [Living] = 500m };

        // Act
        var result = BudgetCalculator.Compute(Tree, expenses, limits);

        // Assert
        var living = Assert.Single(result);
        Assert.Equal(Living, living.CategoryId);
        Assert.Equal(500m, living.Limit);
        Assert.Equal(400m, living.Spent);
    }

    [Fact]
    public void Compute_LeafBudget_CountsOnlyThatCategory()
    {
        // Arrange
        var expenses = new List<(Guid?, decimal)> { (Groceries, 100m), (Rent, 300m) };
        var limits = new Dictionary<Guid, decimal> { [Groceries] = 200m };

        // Act
        var result = BudgetCalculator.Compute(Tree, expenses, limits);

        // Assert — only Groceries counts; Rent is a sibling, not a descendant.
        Assert.Equal(100m, Assert.Single(result).Spent);
    }

    [Fact]
    public void Compute_NoSpending_ReturnsZeroForTheBudget()
    {
        // Act
        var result = BudgetCalculator.Compute(
            Tree,
            [],
            new Dictionary<Guid, decimal> { [Fun] = 100m }
        );

        // Assert
        Assert.Equal(0m, Assert.Single(result).Spent);
    }
}
