using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Planning;

/// <summary>
/// Auto-matching against a real Postgres, focused on the two invariants the
/// multi-match rework must preserve: it stays 1:1 (an occurrence with any
/// existing match, auto or manual, is skipped; an already plan-matched
/// transaction is never offered again), while manual matching elsewhere is
/// free to link many.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchPlannedTransactionsCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private static readonly DateOnly Due = new(DateTime.Today.Year, DateTime.Today.Month, 5);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_MatchingTransaction_CreatesOneMatchAndLink()
    {
        // Arrange
        var itemId = await SeedPlannedItemAsync(-900m);
        var transactionId = await SeedTransactionAsync(-900m, Due);

        // Act
        await RunAsync();

        // Assert
        await using var session = fixture.Store.QuerySession();
        var matches = await session.Query<PlannedMatch>().ToListAsync(Ct);
        var match = Assert.Single(matches);
        Assert.Equal(itemId, match.PlannedItemId);
        Assert.Equal(transactionId, match.TransactionId);
        var view = await session.LoadAsync<TransactionView>(transactionId, Ct);
        Assert.True(view!.IsPlanMatched);
    }

    [Fact]
    public async Task Handle_OccurrenceAlreadyManuallyMatched_IsSkipped()
    {
        // Arrange — a manual pick already satisfies this occurrence; a second,
        // better-fitting transaction must not also get pulled in automatically.
        var itemId = await SeedPlannedItemAsync(-900m);
        var manuallyMatched = await SeedTransactionAsync(-850m, Due);
        var betterFit = await SeedTransactionAsync(-900m, Due);
        await using (var session = fixture.Store.LightweightSession())
        {
            session.Store(
                new PlannedMatch
                {
                    Id = PlannedMatch.KeyFor(itemId, Due, manuallyMatched),
                    PlannedItemId = itemId,
                    DueDate = Due,
                    TransactionId = manuallyMatched,
                    Auto = false,
                }
            );
            await session.SaveChangesAsync(Ct);
        }

        // Act
        await RunAsync();

        // Assert
        await using var query = fixture.Store.QuerySession();
        var matches = await query.Query<PlannedMatch>().ToListAsync(Ct);
        Assert.Single(matches);
        var betterFitView = await query.LoadAsync<TransactionView>(betterFit, Ct);
        Assert.False(betterFitView!.IsPlanMatched);
    }

    [Fact]
    public async Task Handle_TransactionAlreadyPlanMatched_IsNeverOfferedAgain()
    {
        // Arrange — the transaction already satisfies one occurrence (manually);
        // a second, unrelated occurrence must not auto-claim it too.
        var firstItem = await SeedPlannedItemAsync(-900m);
        var secondItem = await SeedPlannedItemAsync(-900m);
        var transactionId = await SeedTransactionAsync(-900m, Due);
        await using (var session = fixture.Store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<TransactionView>(transactionId, Ct);
            stream.AppendOne(new TransactionMatchedToPlannedItem(firstItem, Due));
            session.Store(
                new PlannedMatch
                {
                    Id = PlannedMatch.KeyFor(firstItem, Due, transactionId),
                    PlannedItemId = firstItem,
                    DueDate = Due,
                    TransactionId = transactionId,
                    Auto = false,
                }
            );
            await session.SaveChangesAsync(Ct);
        }

        // Act
        await RunAsync();

        // Assert — the second item's occurrence stays unmatched rather than
        // silently grabbing the already-used transaction.
        await using var query = fixture.Store.QuerySession();
        var secondItemMatched = await query
            .Query<PlannedMatch>()
            .AnyAsync(m => m.PlannedItemId == secondItem, Ct);
        Assert.False(secondItemMatched);
    }

    #region World

    private async Task RunAsync()
    {
        await using var session = fixture.Store.LightweightSession();
        await MatchPlannedTransactionsCommandHandler.Handle(
            new MatchPlannedTransactionsCommand(),
            session,
            NullLogger<MatchPlannedTransactionsCommand>.Instance,
            Ct
        );
    }

    private async Task<Guid> SeedPlannedItemAsync(decimal amount)
    {
        var item = new PlannedItem
        {
            Description = "Test item",
            Amount = amount,
            Schedule = new PlannedSchedule(PlannedFrequency.Monthly, Due),
        };
        await using var session = fixture.Store.LightweightSession();
        session.Store(item);
        await session.SaveChangesAsync(Ct);
        return item.Id;
    }

    private async Task<Guid> SeedTransactionAsync(decimal amount, DateOnly bookingDate)
    {
        var transactionId = Guid.NewGuid();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                Guid.NewGuid(),
                bookingDate,
                ValueDate: null,
                amount,
                "EUR",
                amount,
                Counterparty: "Landlord",
                Description: "rent",
                ExternalId: null,
                DedupHash: Guid.NewGuid().ToString(),
                ImportBatchId: Guid.NewGuid(),
                RawData: null
            )
        );
        await session.SaveChangesAsync(Ct);
        return transactionId;
    }

    #endregion
}
