using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

/// <summary>
/// The one-time backfill for documents written before <c>IsCategorized</c>
/// existed on <see cref="TransactionView"/> — see the fix for a live-database
/// report where the entire pre-migration categorized history reappeared as
/// "uncategorized" in Review after the split-category feature deployed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RepairCategorizedFlagCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_CategoryIdSetButFlagStale_RepairsIt()
    {
        // Arrange — simulates a document written by a build older than the
        // split-category feature: CategoryId set, IsCategorized never
        // persisted alongside it.
        var stale = await SeedTransactionAsync(-23.45m);
        var category = await CreateCategoryAsync();
        await SetLegacyCategorizedStateAsync(stale, category);

        // Act
        var result = await RepairAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(stale);
        Assert.True(view.IsCategorized);
        Assert.Equal(category, view.CategoryId);
    }

    [Fact]
    public async Task Handle_UncategorizedTransaction_IsLeftAlone()
    {
        // Arrange
        var uncategorized = await SeedTransactionAsync(-10m);

        // Act
        var result = await RepairAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(uncategorized);
        Assert.False(view.IsCategorized);
        Assert.Null(view.CategoryId);
    }

    [Fact]
    public async Task Handle_AlreadyCorrectlyFlagged_IsUntouched()
    {
        // Arrange — categorized through the current code path, which always
        // sets CategoryId and IsCategorized together.
        var transactionId = await SeedTransactionAsync(-23.45m);
        var category = await CreateCategoryAsync();
        await using (var session = fixture.Store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<TransactionView>(transactionId, Ct);
            stream.AppendOne(new TransactionCategorized(category, CategorySource.Manual, null));
            await session.SaveChangesAsync(Ct);
        }

        // Act
        var result = await RepairAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(transactionId);
        Assert.True(view.IsCategorized);
        Assert.Equal(category, view.CategoryId);
    }

    [Fact]
    public async Task Handle_RunTwice_IsIdempotent()
    {
        // Arrange
        var stale = await SeedTransactionAsync(-23.45m);
        var category = await CreateCategoryAsync();
        await SetLegacyCategorizedStateAsync(stale, category);
        await RepairAsync();

        // Act — nothing left to repair the second time.
        var result = await RepairAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = await LoadAsync(stale);
        Assert.True(view.IsCategorized);
    }

    #region World

    private async Task<Result> RepairAsync()
    {
        await using var session = fixture.Store.LightweightSession();
        return await RepairCategorizedFlagCommandHandler.Handle(
            new RepairCategorizedFlagCommand(),
            session,
            Ct
        );
    }

    private async Task<TransactionView> LoadAsync(Guid transactionId)
    {
        await using var session = fixture.Store.QuerySession();
        return (await session.LoadAsync<TransactionView>(transactionId, Ct))!;
    }

    /// <summary>
    /// Overwrites the stored projection directly (bypassing the event stream)
    /// to reproduce the exact shape a pre-split-feature deployment left behind:
    /// CategoryId set, IsCategorized false.
    /// </summary>
    private async Task SetLegacyCategorizedStateAsync(Guid transactionId, Guid categoryId)
    {
        var view = await LoadAsync(transactionId);
        view.CategoryId = categoryId;
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
