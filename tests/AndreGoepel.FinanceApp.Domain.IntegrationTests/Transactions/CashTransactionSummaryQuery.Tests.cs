using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

[Collection(IntegrationCollection.Name)]
public sealed class CashTransactionSummaryQueryTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LoadAsync_AggregatesCompleteMonthIndependentlyOfPageSize()
    {
        var accountId = Guid.NewGuid();
        var monthStart = new DateOnly(2026, 9, 1);
        var transactions = Enumerable
            .Range(0, 510)
            .Select(index => Create(accountId, monthStart.AddDays(index % 28), -1m))
            .ToList();
        transactions.Add(Create(accountId, monthStart.AddDays(5), 75m));
        transactions.Add(Create(accountId, monthStart.AddMonths(-1), -500m));
        transactions.Add(Create(Guid.NewGuid(), monthStart.AddDays(5), -500m));
        await StoreAsync(transactions);

        await using var session = fixture.Store.QuerySession();
        var result = await CashTransactionSummaryQuery.LoadAsync(
            session,
            accountId,
            monthStart,
            Ct
        );

        Assert.Equal(510m, result.Spent);
        Assert.Equal(75m, result.Received);
    }

    [Fact]
    public async Task LoadAsync_ReturnsZeroForMonthWithoutTransactions()
    {
        await using var session = fixture.Store.QuerySession();

        var result = await CashTransactionSummaryQuery.LoadAsync(
            session,
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            Ct
        );

        Assert.Equal(0m, result.Spent);
        Assert.Equal(0m, result.Received);
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

    private static TransactionView Create(Guid accountId, DateOnly date, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            BookingDate = date,
            Amount = amount,
            Currency = "EUR",
            Description = "Cash entry",
            DedupHash = Guid.NewGuid().ToString(),
            ImportBatchId = Guid.NewGuid(),
        };
}
