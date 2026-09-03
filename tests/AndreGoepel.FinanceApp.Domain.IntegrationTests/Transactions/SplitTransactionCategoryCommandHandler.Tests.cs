using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

[Collection(IntegrationCollection.Name)]
public sealed class SplitTransactionCategoryCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_OneLine_Fails()
    {
        // Arrange
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();

        // Act
        var result = await SplitAsync(transactionId, [new CategoryLine(groceries, -23.45m)]);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("A split needs at least two categories.", result.Error);
    }

    [Fact]
    public async Task Handle_LinesDoNotSumToTotal_Fails()
    {
        // Arrange
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();

        // Act
        var result = await SplitAsync(
            transactionId,
            [new CategoryLine(groceries, -20.00m), new CategoryLine(electronics, -2.00m)]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "The split amounts must add up to the transaction's total amount.",
            result.Error
        );
    }

    [Fact]
    public async Task Handle_ZeroLineAmount_Fails()
    {
        // Arrange
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();

        // Act
        var result = await SplitAsync(
            transactionId,
            [new CategoryLine(groceries, -23.45m), new CategoryLine(electronics, 0m)]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Each split amount must not be zero.", result.Error);
    }

    [Fact]
    public async Task Handle_LineOppositeSignOfTotal_Fails()
    {
        // Arrange — total is an expense; a positive line makes no sense here.
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();

        // Act
        var result = await SplitAsync(
            transactionId,
            [new CategoryLine(groceries, -30.00m), new CategoryLine(electronics, 6.55m)]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "Each split amount must have the same sign as the transaction (negative = expense, positive = income).",
            result.Error
        );
    }

    [Fact]
    public async Task Handle_UnknownCategory_Fails()
    {
        // Arrange
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();

        // Act
        var result = await SplitAsync(
            transactionId,
            [new CategoryLine(groceries, -20.00m), new CategoryLine(Guid.NewGuid(), -3.45m)]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category not found.", result.Error);
    }

    [Fact]
    public async Task Handle_ValidSplit_UpdatesProjectionAndClearsScalarCategory()
    {
        // Arrange
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();
        var lines = new[]
        {
            new CategoryLine(groceries, -20.00m),
            new CategoryLine(electronics, -3.45m),
        };

        // Act
        var result = await SplitAsync(transactionId, lines);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var view = await session.LoadAsync<TransactionView>(transactionId, Ct);
        Assert.NotNull(view);
        Assert.Null(view!.CategoryId);
        Assert.Equal(lines, view.CategoryLines);
    }

    [Fact]
    public async Task Handle_ValidSplit_RemovesAnyPendingSuggestion()
    {
        // Arrange — a stale AI suggestion must not keep surfacing in the review
        // queue once the transaction has been manually split.
        var (transactionId, _) = await SeedTransactionAsync(-23.45m);
        var groceries = await CreateCategoryAsync();
        var electronics = await CreateCategoryAsync();
        await using (var session = fixture.Store.LightweightSession())
        {
            session.Store(
                new CategorySuggestion
                {
                    Id = transactionId,
                    CategoryId = groceries,
                    Confidence = 0.5m,
                }
            );
            await session.SaveChangesAsync(Ct);
        }

        // Act
        var result = await SplitAsync(
            transactionId,
            [new CategoryLine(groceries, -20.00m), new CategoryLine(electronics, -3.45m)]
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var query = fixture.Store.QuerySession();
        Assert.Null(await query.LoadAsync<CategorySuggestion>(transactionId, Ct));
    }

    #region World

    private async Task<Result> SplitAsync(Guid transactionId, IReadOnlyList<CategoryLine> lines)
    {
        await using var session = fixture.Store.LightweightSession();
        return await SplitTransactionCategoryCommandHandler.Handle(
            new SplitTransactionCategoryCommand(transactionId, lines),
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task<(Guid TransactionId, Guid AccountId)> SeedTransactionAsync(decimal amount)
    {
        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                accountId,
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
        return (transactionId, accountId);
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
