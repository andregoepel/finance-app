using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

[Collection(IntegrationCollection.Name)]
public sealed class TransactionPageQueryTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LoadAsync_ReturnsRequestedPageAndCompleteCount()
    {
        var accountId = Guid.NewGuid();
        var transactions = Enumerable
            .Range(1, 60)
            .Select(index => Create(index, accountId))
            .ToList();
        await StoreAsync(transactions);

        var page = await LoadAsync(
            Filters([accountId]),
            TransactionSort.BookingDate,
            descending: true,
            skip: 25,
            take: 25
        );

        Assert.Equal(60, page.TotalCount);
        Assert.Equal(25, page.Items.Count);
        Assert.Equal(
            transactions
                .OrderByDescending(transaction => transaction.BookingDate)
                .Skip(25)
                .Take(25)
                .Select(transaction => transaction.Id),
            page.Items.Select(transaction => transaction.Id)
        );
    }

    [Fact]
    public async Task LoadAsync_SearchesRowsBeyondFormerLimitBeforePaging()
    {
        var accountId = Guid.NewGuid();
        var transactions = Enumerable
            .Range(1, 1001)
            .Select(index => Create(index, accountId))
            .ToList();
        var oldest = Create(1002, accountId, counterparty: "Unique Needle Merchant");
        transactions.Add(oldest);
        await StoreAsync(transactions);

        var page = await LoadAsync(
            Filters([accountId], searchText: "NEEDLE"),
            TransactionSort.BookingDate,
            descending: true,
            skip: 0,
            take: 25
        );

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(oldest.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task LoadAsync_AppliesAccountDateAndUncategorizedFilters()
    {
        var accountId = Guid.NewGuid();
        var match = Create(5, accountId);
        var wrongAccount = Create(5, Guid.NewGuid());
        var tooOld = Create(20, accountId);
        var categorized = Create(5, accountId, categoryId: Guid.NewGuid());
        await StoreAsync([match, wrongAccount, tooOld, categorized]);

        var page = await LoadAsync(
            new TransactionPageFilters(
                [accountId],
                CategoryIds: null,
                Uncategorized: true,
                From: match.BookingDate,
                To: match.BookingDate,
                SearchText: null
            ),
            TransactionSort.BookingDate,
            descending: true,
            skip: 0,
            take: 25
        );

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task LoadAsync_CategoryFilterIncludesChildrenAndSplitLines()
    {
        var accountId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        var parent = Create(1, accountId, categoryId: parentId);
        var child = Create(2, accountId, categoryId: childId);
        var splitChild = Create(3, accountId);
        splitChild.IsCategorized = true;
        splitChild.CategoryLines =
        [
            new CategoryLine(childId, -5m),
            new CategoryLine(unrelatedId, -5m),
        ];
        var unrelated = Create(4, accountId, categoryId: unrelatedId);
        await StoreAsync([parent, child, splitChild, unrelated]);

        var page = await LoadAsync(
            Filters([accountId], categoryIds: [parentId, childId]),
            TransactionSort.BookingDate,
            descending: true,
            skip: 0,
            take: 25
        );

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([parent.Id, child.Id, splitChild.Id], page.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData(TransactionSort.Counterparty, false)]
    [InlineData(TransactionSort.Description, true)]
    [InlineData(TransactionSort.Amount, false)]
    public async Task LoadAsync_AppliesSupportedSorts(TransactionSort sort, bool descending)
    {
        var accountId = Guid.NewGuid();
        var alpha = Create(1, accountId, amount: 20m, counterparty: "Alpha", description: "Alpha");
        var beta = Create(2, accountId, amount: 10m, counterparty: "Beta", description: "Beta");
        await StoreAsync([alpha, beta]);

        var page = await LoadAsync(Filters([accountId]), sort, descending, skip: 0, take: 25);

        var expectedFirst = sort switch
        {
            TransactionSort.Counterparty => alpha,
            TransactionSort.Description => beta,
            TransactionSort.Amount => beta,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expectedFirst.Id, page.Items[0].Id);
    }

    private async Task<TransactionPage> LoadAsync(
        TransactionPageFilters filters,
        TransactionSort sort,
        bool descending,
        int skip,
        int take
    )
    {
        await using var session = fixture.Store.QuerySession();
        return await TransactionPageQuery.LoadAsync(
            session,
            filters,
            sort,
            descending,
            skip,
            take,
            Ct
        );
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

    private static TransactionPageFilters Filters(
        IReadOnlyList<Guid> accountIds,
        IReadOnlyList<Guid>? categoryIds = null,
        string? searchText = null
    ) => new(accountIds, categoryIds, Uncategorized: false, null, null, searchText);

    private static TransactionView Create(
        int index,
        Guid accountId,
        decimal amount = -10m,
        string counterparty = "Merchant",
        string description = "Description",
        Guid? categoryId = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            BookingDate = new DateOnly(2026, 9, 1).AddDays(-index),
            Amount = amount,
            Currency = "EUR",
            Counterparty = counterparty,
            Description = description,
            DedupHash = Guid.NewGuid().ToString(),
            ImportBatchId = Guid.NewGuid(),
            CategoryId = categoryId,
            IsCategorized = categoryId is not null,
            CategoryLines = categoryId is Guid id ? [new CategoryLine(id, amount)] : [],
        };
}
