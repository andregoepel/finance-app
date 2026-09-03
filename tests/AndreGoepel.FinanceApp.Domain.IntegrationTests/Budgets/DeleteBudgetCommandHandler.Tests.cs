using AndreGoepel.FinanceApp.Domain.Budgets;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Budgets;

/// <summary>
/// Deleting one budget period must not disturb any other period on the same
/// category. Runs against a real Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeleteBudgetCommandHandlerTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_RemovesTheBudget()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();
        var budgetId = await CreateBudgetAsync(categoryId, new DateOnly(2026, 1, 1));

        // Act
        var result = await DeleteAsync(budgetId);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<Budget>(budgetId, Ct));
    }

    [Fact]
    public async Task Handle_OtherPeriodsForTheSameCategory_StayUntouched()
    {
        // Arrange — two non-overlapping periods on the same category.
        var categoryId = await CreateCategoryAsync();
        var kept = await CreateBudgetAsync(categoryId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1));
        var removed = await CreateBudgetAsync(categoryId, new DateOnly(2026, 7, 1));

        // Act
        await DeleteAsync(removed);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var remaining = await session.Query<Budget>().ToListAsync(Ct);
        Assert.Equal(kept, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task Handle_UnknownBudget_Succeeds()
    {
        // Act — deleting an already-gone budget is idempotent, like the rest of the app's deletes.
        var result = await DeleteAsync(Guid.NewGuid());

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    #region World

    private async Task<AndreGoepel.Core.Result> DeleteAsync(Guid budgetId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await DeleteBudgetCommandHandler.Handle(new DeleteBudgetCommand(budgetId), session, Ct);
    }

    private async Task<Guid> CreateBudgetAsync(Guid categoryId, DateOnly startMonth, DateOnly? endMonth = null)
    {
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            MonthlyLimit = 200m,
            StartMonth = startMonth,
            EndMonth = endMonth,
        };
        await using var session = fixture.Store.LightweightSession();
        session.Store(budget);
        await session.SaveChangesAsync(Ct);
        return budget.Id;
    }

    private async Task<Guid> CreateCategoryAsync()
    {
        var category = new Category { Name = "Groceries" };
        await using var session = fixture.Store.LightweightSession();
        session.Store(category);
        await session.SaveChangesAsync(Ct);
        return category.Id;
    }

    #endregion
}
