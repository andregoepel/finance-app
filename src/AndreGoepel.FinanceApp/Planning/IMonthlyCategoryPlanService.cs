namespace AndreGoepel.FinanceApp.Planning;

public interface IMonthlyCategoryPlanService
{
    Task<IReadOnlyList<MonthlyCategoryPlan>> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    );
}

public sealed record MonthlyCategoryPlan(
    Guid? CategoryId,
    string? Category,
    decimal? BudgetLimit,
    decimal ActualSpent,
    decimal PlannedRemaining
)
{
    public decimal ForecastSpent => ActualSpent + PlannedRemaining;

    public decimal? FlexibleRemaining => BudgetLimit - ForecastSpent;

    public decimal Percent =>
        BudgetLimit is > 0 ? Math.Round(ForecastSpent / BudgetLimit.Value * 100, 0) : 0;

    public bool IsOver => BudgetLimit is decimal limit && ForecastSpent > limit;
}
