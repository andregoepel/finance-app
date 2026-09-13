namespace AndreGoepel.FinanceApp.Planning;

public interface IMonthlyCategoryPlanService
{
    Task<IReadOnlyList<MonthlyCategoryPlan>> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? accountIds = null
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
    public decimal ForecastSpent => Math.Max(ActualSpent + PlannedRemaining, BudgetLimit ?? 0m);

    public decimal ForecastRemaining => Math.Max(0m, ForecastSpent - ActualSpent);

    public decimal? FlexibleRemaining => BudgetLimit - ActualSpent - PlannedRemaining;

    public decimal Percent =>
        BudgetLimit is > 0 ? Math.Round(ForecastSpent / BudgetLimit.Value * 100, 0) : 0;

    public bool IsOver => BudgetLimit is decimal limit && ForecastSpent > limit;
}
