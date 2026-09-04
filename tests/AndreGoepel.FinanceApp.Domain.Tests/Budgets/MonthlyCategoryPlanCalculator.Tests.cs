using AndreGoepel.FinanceApp.Domain.Budgets;

namespace AndreGoepel.FinanceApp.Domain.Tests.Budgets;

public sealed class MonthlyCategoryPlanCalculatorTests
{
    private readonly Guid _housing = Guid.NewGuid();
    private readonly Guid _rent = Guid.NewGuid();
    private readonly Guid _utilities = Guid.NewGuid();

    [Fact]
    public void Compute_AllocatesDescendantsToNearestBudgetWithoutDoubleCounting()
    {
        var parents = new Dictionary<Guid, Guid?>
        {
            [_housing] = null,
            [_rent] = _housing,
            [_utilities] = _housing,
        };

        var result = MonthlyCategoryPlanCalculator.Compute(
            parents,
            [(_rent, 1_000m), (_utilities, 100m)],
            [(_rent, 1_200m), (_utilities, 150m)],
            new Dictionary<Guid, decimal> { [_housing] = 1_500m }
        );

        var plan = Assert.Single(result);
        Assert.Equal(_housing, plan.CategoryId);
        Assert.Equal(1_500m, plan.BudgetLimit);
        Assert.Equal(1_100m, plan.ActualSpent);
        Assert.Equal(1_350m, plan.PlannedRemaining);
        Assert.Equal(2_450m, plan.ForecastSpent);
        Assert.Equal(-950m, plan.FlexibleRemaining);
    }

    [Fact]
    public void Compute_KeepsPlannedOnlyAndUnallocatedCommitmentsVisible()
    {
        var result = MonthlyCategoryPlanCalculator.Compute(
            new Dictionary<Guid, Guid?> { [_rent] = null },
            [(_rent, 50m), (null, 20m)],
            [(_rent, 800m), (null, 75m)],
            new Dictionary<Guid, decimal>()
        );

        var rent = Assert.Single(result, row => row.CategoryId == _rent);
        Assert.Null(rent.BudgetLimit);
        Assert.Equal(50m, rent.ActualSpent);
        Assert.Equal(800m, rent.PlannedRemaining);

        var unallocated = Assert.Single(result, row => row.CategoryId is null);
        Assert.Equal(20m, unallocated.ActualSpent);
        Assert.Equal(75m, unallocated.PlannedRemaining);
    }

    [Fact]
    public void Compute_UsesNearestNestedBudget()
    {
        var parents = new Dictionary<Guid, Guid?> { [_housing] = null, [_rent] = _housing };

        var result = MonthlyCategoryPlanCalculator.Compute(
            parents,
            [],
            [(_rent, 800m)],
            new Dictionary<Guid, decimal> { [_housing] = 1_500m, [_rent] = 900m }
        );

        Assert.Equal(800m, Assert.Single(result, row => row.CategoryId == _rent).PlannedRemaining);
        Assert.Equal(0m, Assert.Single(result, row => row.CategoryId == _housing).PlannedRemaining);
    }
}
