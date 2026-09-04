using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

[Collection(IntegrationCollection.Name)]
public sealed class CategorizeTransactionCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_FirstAssignment_SetsCategoryAndIsCategorized()
    {
        // Arrange
        var transactionId = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();

        // Act
        var result = await CategorizeAsync(transactionId, groceries);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(transactionId);
        Assert.Equal(groceries, view.CategoryId);
        Assert.True(view.IsCategorized);
        Assert.Equal(CategorySource.Manual, view.CategorySource);
    }

    [Fact]
    public async Task Handle_SameCategoryAlreadyFlaggedCategorized_IsANoOp()
    {
        // Arrange
        var transactionId = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        await CategorizeAsync(transactionId, groceries);
        var eventCountBefore = await CountEventsAsync(transactionId);

        // Act — re-picking the same, already-correct category from the UI.
        var result = await CategorizeAsync(transactionId, groceries);

        // Assert — nothing new appended; a genuine no-op stays cheap.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(eventCountBefore, await CountEventsAsync(transactionId));
    }

    [Fact]
    public async Task Handle_SameCategoryButIsCategorizedFlagStale_RepairsTheFlag()
    {
        // Arrange — mirrors a document written by a build that predates the
        // split-category feature: CategoryId is set, but IsCategorized never
        // got persisted alongside it. Simulated by overwriting the stored
        // projection directly, bypassing the event stream, the same way an
        // older Apply() implementation would have written it.
        var transactionId = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        await CategorizeAsync(transactionId, groceries);
        await CorruptIsCategorizedFlagAsync(transactionId);
        var corrupted = await LoadAsync(transactionId);
        Assert.False(corrupted.IsCategorized);

        // Act — the user re-picks the same category they already see in
        // Transactions, trying to make the stuck review-queue entry go away.
        var result = await CategorizeAsync(transactionId, groceries);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var repaired = await LoadAsync(transactionId);
        Assert.True(repaired.IsCategorized);
        Assert.Equal(groceries, repaired.CategoryId);
    }

    [Fact]
    public async Task Handle_DifferentCategory_EmitsCorrection()
    {
        // Arrange
        var transactionId = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();
        await CategorizeAsync(transactionId, groceries);

        // Act
        var result = await CategorizeAsync(transactionId, electronics);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(transactionId);
        Assert.Equal(electronics, view.CategoryId);
        Assert.True(view.IsCategorized);
    }

    #region World

    private async Task<Result> CategorizeAsync(Guid transactionId, Guid categoryId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await CategorizeTransactionCommandHandler.Handle(
            new CategorizeTransactionCommand(transactionId, categoryId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task<TransactionView> LoadAsync(Guid transactionId)
    {
        await using var session = fixture.Store.QuerySession();
        return (await session.LoadAsync<TransactionView>(transactionId, Ct))!;
    }

    private async Task<int> CountEventsAsync(Guid transactionId)
    {
        await using var session = fixture.Store.QuerySession();
        var events = await session.Events.FetchStreamAsync(transactionId, token: Ct);
        return events.Count;
    }

    /// <summary>
    /// Overwrites the stored projection directly (bypassing the event stream)
    /// to reproduce a document written before <see cref="TransactionView.IsCategorized"/>
    /// existed — the exact shape a stale deployment left in production.
    /// </summary>
    private async Task CorruptIsCategorizedFlagAsync(Guid transactionId)
    {
        var view = await LoadAsync(transactionId);
        view.IsCategorized = false;
        await using var session = fixture.Store.LightweightSession();
        session.Store(view);
        await session.SaveChangesAsync(Ct);
    }

    private async Task<Guid> SeedTransactionAsync(decimal amount)
    {
        var transactionId = Guid.NewGuid();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                Guid.NewGuid(),
                new DateOnly(2026, 9, 3),
                ValueDate: null,
                amount,
                "EUR",
                amount,
                Counterparty: "REWE",
                Description: "REWE SAGT DANKE",
                ExternalId: null,
                DedupHash: Guid.NewGuid().ToString(),
                ImportBatchId: Guid.NewGuid(),
                RawData: null
            )
        );
        await session.SaveChangesAsync(Ct);
        return transactionId;
    }

    private async Task<Guid> CreateCategoryAsync()
    {
        var category = new Category { Name = Guid.NewGuid().ToString() };
        await using var session = fixture.Store.LightweightSession();
        session.Store(category);
        await session.SaveChangesAsync(Ct);
        return category.Id;
    }

    #endregion
}
