using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Budgets;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Budgets;

/// <summary>
/// A category can hold several budget periods over time, but never two that cover
/// the same month — <see cref="SetBudgetCommandHandler"/> is where that invariant is
/// enforced. Runs against a real Postgres because the overlap check reads back other
/// rows for the same category.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class SetBudgetCommandHandlerTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_NonPositiveLimit_Fails()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();

        // Act
        var result = await SetAsync(Command(categoryId) with { MonthlyLimit = 0m });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("A budget limit must be greater than zero.", result.Error);
    }

    [Fact]
    public async Task Handle_UnknownCategory_Fails()
    {
        // Act
        var result = await SetAsync(Command(Guid.NewGuid()));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category not found.", result.Error);
    }

    [Fact]
    public async Task Handle_EndBeforeStart_Fails()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();

        // Act
        var result = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 6, 1),
                EndMonth = new DateOnly(2026, 3, 1),
            }
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("The end month cannot be before the start month.", result.Error);
    }

    [Fact]
    public async Task Handle_UnknownBudgetId_Fails()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();

        // Act
        var result = await SetAsync(Command(categoryId) with { Id = Guid.NewGuid() });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Budget not found.", result.Error);
    }

    [Fact]
    public async Task Handle_CreatesBudget_FlooringDatesToMonthStart()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();

        // Act
        var result = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 3, 17),
                EndMonth = new DateOnly(2026, 8, 28),
            }
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var budget = result.Value!;
        Assert.Equal(categoryId, budget.CategoryId);
        Assert.Equal(new DateOnly(2026, 3, 1), budget.StartMonth);
        Assert.Equal(new DateOnly(2026, 8, 1), budget.EndMonth);
    }

    [Fact]
    public async Task Handle_OverlappingPeriod_Fails()
    {
        // Arrange — Jan-Jun already budgeted; a new period touching April overlaps it.
        var categoryId = await CreateCategoryAsync();
        await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 1, 1),
                EndMonth = new DateOnly(2026, 6, 1),
            }
        );

        // Act
        var result = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 4, 1),
                EndMonth = new DateOnly(2026, 12, 1),
            }
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("This category already has a budget for part of that period.", result.Error);
    }

    [Fact]
    public async Task Handle_AdjacentNonOverlappingPeriod_Succeeds()
    {
        // Arrange — Jan-Jun already budgeted; Jul onward (ongoing) does not overlap it.
        var categoryId = await CreateCategoryAsync();
        await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 1, 1),
                EndMonth = new DateOnly(2026, 6, 1),
            }
        );

        // Act
        var result = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 7, 1),
                EndMonth = null,
            }
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Handle_OpenEndedPeriod_BlocksAnyLaterPeriod()
    {
        // Arrange — an ongoing budget starting Jan has no end, so nothing later can be added.
        var categoryId = await CreateCategoryAsync();
        await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 1, 1),
                EndMonth = null,
            }
        );

        // Act
        var result = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2027, 1, 1),
                EndMonth = null,
            }
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("This category already has a budget for part of that period.", result.Error);
    }

    [Fact]
    public async Task Handle_EditingExistingPeriod_DoesNotConflictWithItself()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync();
        var created = await SetAsync(
            Command(categoryId) with
            {
                StartMonth = new DateOnly(2026, 1, 1),
                EndMonth = new DateOnly(2026, 6, 1),
            }
        );

        // Act — same period, just a higher limit.
        var result = await SetAsync(
            Command(categoryId) with
            {
                Id = created.Value!.Id,
                MonthlyLimit = 250m,
                StartMonth = new DateOnly(2026, 1, 1),
                EndMonth = new DateOnly(2026, 6, 1),
            }
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(created.Value.Id, result.Value!.Id);
        Assert.Equal(250m, result.Value.MonthlyLimit);

        await using var session = fixture.Store.QuerySession();
        Assert.Single(await session.Query<Budget>().ToListAsync(Ct));
    }

    #region World

    private static SetBudgetCommand Command(Guid categoryId) =>
        new(
            Id: null,
            CategoryId: categoryId,
            MonthlyLimit: 200m,
            StartMonth: new DateOnly(2026, 1, 1),
            EndMonth: null
        );

    private async Task<Result<Budget>> SetAsync(SetBudgetCommand command)
    {
        await using var session = fixture.Store.LightweightSession();
        return await SetBudgetCommandHandler.Handle(command, session, DomainLocalizer.Instance, Ct);
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
