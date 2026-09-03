using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Planning;

/// <summary>
/// <see cref="SetPlannedMatchCommand"/> and <see cref="ClearPlannedMatchCommand"/>
/// against a real Postgres: an occurrence can be satisfied by more than one
/// transaction (a salary paid out in two bookings) and a transaction can satisfy
/// more than one occurrence (one transfer covering rent and a car payment).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlannedMatchCommandsTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Due = new(2026, 6, 1);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_UnknownPlannedItem_Fails()
    {
        // Arrange
        var transactionId = await SeedTransactionAsync(-900m);

        // Act
        var result = await SetAsync(Guid.NewGuid(), Due, transactionId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Planned item not found.", result.Error);
    }

    [Fact]
    public async Task Set_UnknownTransaction_Fails()
    {
        // Arrange
        var itemId = await SeedPlannedItemAsync(-900m);

        // Act
        var result = await SetAsync(itemId, Due, Guid.NewGuid());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Transaction not found.", result.Error);
    }

    [Fact]
    public async Task Set_TwoTransactionsOnOneOccurrence_KeepsBothLinked()
    {
        // Arrange — salary split across two bookings.
        var itemId = await SeedPlannedItemAsync(1000m);
        var first = await SeedTransactionAsync(600m);
        var second = await SeedTransactionAsync(400m);

        // Act
        var firstResult = await SetAsync(itemId, Due, first);
        var secondResult = await SetAsync(itemId, Due, second);

        // Assert
        Assert.True(firstResult.IsSuccess, firstResult.Error);
        Assert.True(secondResult.IsSuccess, secondResult.Error);
        await using var session = fixture.Store.QuerySession();
        var matches = await session
            .Query<PlannedMatch>()
            .Where(m => m.PlannedItemId == itemId && m.DueDate == Due)
            .ToListAsync(Ct);
        var transactionIds = matches.Select(m => m.TransactionId).ToList();
        Assert.Equal(2, transactionIds.Count);
        Assert.Contains(first, transactionIds);
        Assert.Contains(second, transactionIds);
        var firstView = await session.LoadAsync<TransactionView>(first, Ct);
        Assert.True(firstView!.IsPlanMatched);
        Assert.Equal([new PlannedLink(itemId, Due)], firstView.PlannedLinks);
    }

    [Fact]
    public async Task Set_OneTransactionOnTwoOccurrences_KeepsBothLinked()
    {
        // Arrange — one transfer covering rent and a car payment.
        var rent = await SeedPlannedItemAsync(-900m);
        var car = await SeedPlannedItemAsync(-300m);
        var transaction = await SeedTransactionAsync(-1200m);

        // Act
        var rentResult = await SetAsync(rent, Due, transaction);
        var carResult = await SetAsync(car, Due, transaction);

        // Assert
        Assert.True(rentResult.IsSuccess, rentResult.Error);
        Assert.True(carResult.IsSuccess, carResult.Error);
        await using var session = fixture.Store.QuerySession();
        var view = await session.LoadAsync<TransactionView>(transaction, Ct);
        Assert.Equal(2, view!.PlannedLinks.Count);
        Assert.Contains(new PlannedLink(rent, Due), view.PlannedLinks);
        Assert.Contains(new PlannedLink(car, Due), view.PlannedLinks);
        var matches = await session
            .Query<PlannedMatch>()
            .Where(m => m.TransactionId == transaction)
            .ToListAsync(Ct);
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task Set_SamePairingTwice_DoesNotDuplicate()
    {
        // Arrange
        var itemId = await SeedPlannedItemAsync(-900m);
        var transactionId = await SeedTransactionAsync(-900m);
        await SetAsync(itemId, Due, transactionId);

        // Act — re-matching (e.g. a repeated "Use" click).
        var result = await SetAsync(itemId, Due, transactionId);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var matches = await session
            .Query<PlannedMatch>()
            .Where(m => m.PlannedItemId == itemId && m.DueDate == Due)
            .ToListAsync(Ct);
        Assert.Single(matches);
        var view = await session.LoadAsync<TransactionView>(transactionId, Ct);
        Assert.Single(view!.PlannedLinks);
    }

    [Fact]
    public async Task Clear_OneOfTwoLinesOnAnOccurrence_LeavesTheOtherIntact()
    {
        // Arrange
        var itemId = await SeedPlannedItemAsync(1000m);
        var first = await SeedTransactionAsync(600m);
        var second = await SeedTransactionAsync(400m);
        await SetAsync(itemId, Due, first);
        await SetAsync(itemId, Due, second);

        // Act
        var result = await ClearAsync(itemId, Due, first);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var matches = await session
            .Query<PlannedMatch>()
            .Where(m => m.PlannedItemId == itemId && m.DueDate == Due)
            .ToListAsync(Ct);
        Assert.Equal([second], matches.Select(m => m.TransactionId));
        var firstView = await session.LoadAsync<TransactionView>(first, Ct);
        Assert.False(firstView!.IsPlanMatched);
        var secondView = await session.LoadAsync<TransactionView>(second, Ct);
        Assert.True(secondView!.IsPlanMatched);
    }

    [Fact]
    public async Task Clear_OneOfTwoLinksOnATransaction_LeavesTheOtherIntact()
    {
        // Arrange
        var rent = await SeedPlannedItemAsync(-900m);
        var car = await SeedPlannedItemAsync(-300m);
        var transaction = await SeedTransactionAsync(-1200m);
        await SetAsync(rent, Due, transaction);
        await SetAsync(car, Due, transaction);

        // Act
        var result = await ClearAsync(rent, Due, transaction);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var view = await session.LoadAsync<TransactionView>(transaction, Ct);
        Assert.Equal([new PlannedLink(car, Due)], view!.PlannedLinks);
        Assert.True(view.IsPlanMatched);
    }

    [Fact]
    public async Task Clear_NoMatchingLine_SucceedsAsNoOp()
    {
        // Act
        var result = await ClearAsync(Guid.NewGuid(), Due, Guid.NewGuid());

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    #region World

    private async Task<Result> SetAsync(Guid plannedItemId, DateOnly due, Guid transactionId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await SetPlannedMatchCommandHandler.Handle(
            new SetPlannedMatchCommand(plannedItemId, due, transactionId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task<Result> ClearAsync(Guid plannedItemId, DateOnly due, Guid transactionId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await ClearPlannedMatchCommandHandler.Handle(
            new ClearPlannedMatchCommand(plannedItemId, due, transactionId),
            session,
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

    private async Task<Guid> SeedTransactionAsync(decimal amount)
    {
        var transactionId = Guid.NewGuid();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                Guid.NewGuid(),
                Due,
                ValueDate: null,
                amount,
                "EUR",
                amount,
                Counterparty: "Employer",
                Description: "payout",
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
