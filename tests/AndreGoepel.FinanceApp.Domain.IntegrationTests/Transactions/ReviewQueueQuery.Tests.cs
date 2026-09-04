using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

[Collection(IntegrationCollection.Name)]
public sealed class ReviewQueueQueryTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LoadAsync_ReturnsOnlyRequestedPageAndCompleteCount()
    {
        // Arrange
        var transactions = Enumerable.Range(1, 60).Select(index => Create(index)).ToList();
        await StoreAsync(transactions);

        // Act
        await using var session = fixture.Store.QuerySession();
        var page = await ReviewQueueQuery.LoadAsync(
            session,
            new ReviewQueueFilters(null, null, null, null, null),
            skip: 25,
            take: 25,
            Ct
        );

        // Assert
        Assert.Equal(60, page.TotalCount);
        Assert.Equal(25, page.Items.Count);
        Assert.Equal(
            transactions.OrderByDescending(t => t.BookingDate).Skip(25).Take(25).Select(t => t.Id),
            page.Items.Select(t => t.Id)
        );
    }

    [Fact]
    public async Task LoadAsync_SearchesAllRowsBeforePagingCaseInsensitively()
    {
        // Arrange — the matching row is older than the 500 rows the Review page used to load.
        var transactions = Enumerable.Range(1, 510).Select(index => Create(index)).ToList();
        var oldest = Create(511, counterparty: "Unique Needle Merchant");
        transactions.Add(oldest);
        await StoreAsync(transactions);

        // Act
        await using var session = fixture.Store.QuerySession();
        var page = await ReviewQueueQuery.LoadAsync(
            session,
            new ReviewQueueFilters(null, null, null, null, "NEEDLE"),
            skip: 0,
            take: 25,
            Ct
        );

        // Assert
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(oldest.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task LoadAsync_AppliesReviewAndUserFiltersToRowsAndCount()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var match = Create(1, accountId, amount: 42m, description: "Annual bonus");
        var wrongAccount = Create(2, Guid.NewGuid(), amount: 42m, description: "Annual bonus");
        var expense = Create(3, accountId, amount: -42m, description: "Annual bonus");
        var categorized = Create(4, accountId, amount: 42m, description: "Annual bonus");
        categorized.IsCategorized = true;
        var transfer = Create(5, accountId, amount: 42m, description: "Annual bonus");
        transfer.TransferCounterpartId = Guid.NewGuid();
        await StoreAsync([match, wrongAccount, expense, categorized, transfer]);

        // Act
        await using var session = fixture.Store.QuerySession();
        var page = await ReviewQueueQuery.LoadAsync(
            session,
            new ReviewQueueFilters(
                accountId,
                match.BookingDate,
                match.BookingDate,
                Income: true,
                SearchText: "BONUS"
            ),
            skip: 0,
            take: 25,
            Ct
        );

        // Assert
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match.Id, Assert.Single(page.Items).Id);
    }

    private async Task StoreAsync(IReadOnlyList<TransactionView> transactions)
    {
        await using var session = fixture.Store.LightweightSession();
        foreach (var transaction in transactions)
        {
            session.Store(transaction);
        }
        await session.SaveChangesAsync(Ct);
    }

    private static TransactionView Create(
        int index,
        Guid? accountId = null,
        decimal amount = -10m,
        string counterparty = "Merchant",
        string description = "Description"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId ?? Guid.NewGuid(),
            BookingDate = new DateOnly(2026, 9, 1).AddDays(-index),
            Amount = amount,
            Currency = "EUR",
            Counterparty = counterparty,
            Description = description,
            DedupHash = Guid.NewGuid().ToString(),
            ImportBatchId = Guid.NewGuid(),
        };
}
