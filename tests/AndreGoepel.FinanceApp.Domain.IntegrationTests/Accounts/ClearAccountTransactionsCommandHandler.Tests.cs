using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Accounts;

/// <summary>
/// Clearing an account's history is the same cascade as the hard delete minus the
/// account, so what matters is both halves: that the history really goes, and that
/// everything belonging to the account itself stays. Runs against a real Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ClearAccountTransactionsCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_UnknownAccount_Fails()
    {
        // Act
        await using var session = fixture.Store.LightweightSession();
        var result = await ClearAccountTransactionsCommandHandler.Handle(
            new ClearAccountTransactionsCommand(Guid.NewGuid()),
            session,
            DomainLocalizer.Instance,
            Ct
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Account not found.", result.Error);
    }

    [Fact]
    public async Task Handle_AccountWithoutHistory_ReportsNothingAndKeepsTheAccount()
    {
        // Arrange
        var accountId = await CreateAccountAsync("Empty");

        // Act
        var impact = await ClearAsync(accountId);

        // Assert
        Assert.True(impact.IsEmpty);
        await using var session = fixture.Store.QuerySession();
        Assert.NotNull(await session.LoadAsync<Account>(accountId, Ct));
    }

    [Fact]
    public async Task Handle_DeletesTransactionStreamsAndProjections()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await ClearAsync(world.AccountId);

        // Assert
        Assert.Equal(2, impact.Transactions);
        await using var session = fixture.Store.QuerySession();
        foreach (var transactionId in world.TransactionIds)
        {
            Assert.Null(await session.LoadAsync<TransactionView>(transactionId, Ct));
            Assert.Empty(await session.Events.FetchStreamAsync(transactionId, token: Ct));
        }
    }

    [Fact]
    public async Task Handle_KeepsTheAccountItsBalanceAndItsCryptoHoldings()
    {
        // Arrange — the whole point of this command: the account survives intact.
        var world = await CreateWorldAsync();

        // Act
        await ClearAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var account = await session.LoadAsync<Account>(world.AccountId, Ct);
        Assert.NotNull(account);
        Assert.Equal(1234.56m, account.CurrentBalance);
        var holdings = await session.Query<CryptoHolding>().ToListAsync(Ct);
        Assert.Equal(2, holdings.Count);
    }

    [Fact]
    public async Task Handle_KeepsPlannedItemsExpectingTheAccount()
    {
        // Arrange — the account is still there, so the expectation stays valid.
        var world = await CreateWorldAsync();

        // Act
        await ClearAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var item = await session.LoadAsync<PlannedItem>(world.PlannedItemId, Ct);
        Assert.NotNull(item);
        Assert.Equal(world.AccountId, item.ExpectedAccountId);
    }

    [Fact]
    public async Task Handle_DeletesImportBatchesOfThatAccountOnly()
    {
        // Arrange — dropping these is what lets the next sync backfill from scratch.
        var world = await CreateWorldAsync();

        // Act
        var impact = await ClearAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.ImportBatches);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<ImportBatch>(world.ImportBatchId, Ct));
        Assert.NotNull(await session.LoadAsync<ImportBatch>(world.OtherImportBatchId, Ct));
    }

    [Fact]
    public async Task Handle_TransferCounterpart_IsUnlinkedNotDeleted()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await ClearAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.TransfersUnlinked);
        await using var session = fixture.Store.QuerySession();
        var counterpart = await session.LoadAsync<TransactionView>(world.CounterpartId, Ct);
        Assert.NotNull(counterpart);
        Assert.Null(counterpart.TransferCounterpartId);
        Assert.NotEmpty(await session.Events.FetchStreamAsync(world.CounterpartId, token: Ct));
    }

    [Fact]
    public async Task Handle_PlannedMatchesAndReviewEntries_AreClearedForThoseTransactionsOnly()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await ClearAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.PlannedMatchesCleared);
        Assert.Equal(1, impact.ReviewQueueEntries);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<PlannedMatch>(world.PlannedMatchId, Ct));
        Assert.NotNull(await session.LoadAsync<PlannedMatch>(world.OtherPlannedMatchId, Ct));
        Assert.Null(await session.LoadAsync<CategorySuggestion>(world.GroceriesId, Ct));
        Assert.NotNull(await session.LoadAsync<CategorySuggestion>(world.CounterpartId, Ct));
    }

    [Fact]
    public async Task Handle_OtherAccountsHistory_IsUntouched()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        await ClearAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var remaining = await session.Query<TransactionView>().ToListAsync(Ct);
        Assert.Equal(world.CounterpartId, Assert.Single(remaining).Id);
    }

    [Fact]
    public async Task Handle_RunTwice_IsIdempotent()
    {
        // Arrange — re-running after a clear must not fail on already-gone rows.
        var world = await CreateWorldAsync();
        await ClearAsync(world.AccountId);

        // Act
        var second = await ClearAsync(world.AccountId);

        // Assert
        Assert.True(second.IsEmpty);
    }

    [Fact]
    public async Task Handle_LearnedCategoryRules_Survive()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        await ClearAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var rule = await session.LoadAsync<CategoryRule>(world.CategoryRuleId, Ct);
        Assert.NotNull(rule);
        Assert.Equal(CategoryRuleSource.LearnedFromCorrection, rule.Source);
    }

    [Fact]
    public async Task Preview_MatchesWhatTheClearReports()
    {
        // Arrange
        var world = await CreateWorldAsync();
        await using var previewSession = fixture.Store.QuerySession();
        var preview = await AccountTransactionsClearPreview.ForAccountAsync(
            previewSession,
            world.AccountId,
            DomainLocalizer.Instance,
            Ct
        );

        // Act
        var impact = await ClearAsync(world.AccountId);

        // Assert
        Assert.True(preview.IsSuccess);
        Assert.Equal(preview.Value, impact);
    }

    [Fact]
    public async Task Preview_UnknownAccount_Fails()
    {
        // Act
        await using var session = fixture.Store.QuerySession();
        var result = await AccountTransactionsClearPreview.ForAccountAsync(
            session,
            Guid.NewGuid(),
            DomainLocalizer.Instance,
            Ct
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    #region World

    private async Task<AccountTransactionsClearedImpact> ClearAsync(Guid accountId)
    {
        await using var session = fixture.Store.LightweightSession();
        var result = await ClearAccountTransactionsCommandHandler.Handle(
            new ClearAccountTransactionsCommand(accountId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    /// <summary>
    /// Mirrors the hard-delete suite's world so the two can be compared: two
    /// accounts, the cleared one owning two transactions (one a transfer leg), an
    /// import batch, a crypto holding, a planned match and a review-queue entry;
    /// the other owning the transfer counterpart and a copy of each.
    /// </summary>
    private async Task<World> CreateWorldAsync()
    {
        var accountId = await CreateAccountAsync("Wise");
        var otherAccountId = await CreateAccountAsync("DKB");

        var importBatchId = await CreateImportBatchAsync(accountId);
        var otherImportBatchId = await CreateImportBatchAsync(otherAccountId);

        var transferLeg = await ImportTransactionAsync(accountId, importBatchId, "rent transfer");
        var groceries = await ImportTransactionAsync(accountId, importBatchId, "groceries");
        var counterpartId = await ImportTransactionAsync(
            otherAccountId,
            otherImportBatchId,
            "rent transfer"
        );
        await LinkAsTransferAsync(transferLeg, counterpartId);

        var plannedItemId = await CreatePlannedItemAsync(accountId);
        var plannedMatchId = await CreatePlannedMatchAsync(plannedItemId, 1, transferLeg);
        var otherPlannedMatchId = await CreatePlannedMatchAsync(plannedItemId, 2, counterpartId);

        await StoreAsync(
            new CategorySuggestion
            {
                Id = groceries,
                CategoryId = Guid.NewGuid(),
                Confidence = 0.4m,
            },
            new CategorySuggestion
            {
                Id = counterpartId,
                CategoryId = Guid.NewGuid(),
                Confidence = 0.4m,
            }
        );

        var learnedRule = new CategoryRule
        {
            CategoryId = Guid.NewGuid(),
            CounterpartyContains = "Cafe",
            Source = CategoryRuleSource.LearnedFromCorrection,
        };
        await StoreAsync(
            CryptoHoldingFor(accountId),
            CryptoHoldingFor(otherAccountId),
            learnedRule
        );

        return new World(
            accountId,
            transferLeg,
            groceries,
            counterpartId,
            importBatchId,
            otherImportBatchId,
            plannedItemId,
            plannedMatchId,
            otherPlannedMatchId,
            learnedRule.Id
        );
    }

    private sealed record World(
        Guid AccountId,
        Guid TransferLegId,
        Guid GroceriesId,
        Guid CounterpartId,
        Guid ImportBatchId,
        Guid OtherImportBatchId,
        Guid PlannedItemId,
        string PlannedMatchId,
        string OtherPlannedMatchId,
        Guid CategoryRuleId
    )
    {
        public IReadOnlyList<Guid> TransactionIds => [TransferLegId, GroceriesId];
    }

    private static CryptoHolding CryptoHoldingFor(Guid accountId) =>
        new()
        {
            Id = CryptoHolding.KeyFor(accountId, "bitcoin"),
            AccountId = accountId,
            Symbol = "BTC",
            CoinGeckoId = "bitcoin",
            Quantity = 0.25m,
        };

    private async Task<Guid> CreateAccountAsync(string name)
    {
        var account = new Account
        {
            Name = name,
            Provider = ProviderKind.Wise,
            Type = AccountType.Checking,
            Currency = "EUR",
            SyncMethod = SyncMethod.CsvUpload,
            CurrentBalance = 1234.56m,
        };
        await StoreAsync(account);
        return account.Id;
    }

    private async Task<Guid> CreateImportBatchAsync(Guid accountId)
    {
        var batch = new ImportBatch
        {
            AccountId = accountId,
            Provider = ProviderKind.Wise,
            Source = "statement.csv",
            ParserId = "wise-csv-v1",
        };
        await StoreAsync(batch);
        return batch.Id;
    }

    private async Task<Guid> CreatePlannedItemAsync(Guid expectedAccountId)
    {
        var item = new PlannedItem
        {
            Description = "Rent",
            Amount = -900m,
            Schedule = new PlannedSchedule(PlannedFrequency.Monthly, new DateOnly(2026, 1, 1)),
            ExpectedAccountId = expectedAccountId,
        };
        await StoreAsync(item);
        return item.Id;
    }

    private async Task<string> CreatePlannedMatchAsync(
        Guid plannedItemId,
        int month,
        Guid transactionId
    )
    {
        var due = new DateOnly(2026, month, 1);
        var match = new PlannedMatch
        {
            Id = PlannedMatch.KeyFor(plannedItemId, due),
            PlannedItemId = plannedItemId,
            DueDate = due,
            TransactionId = transactionId,
            Auto = true,
        };
        await StoreAsync(match);
        return match.Id;
    }

    private async Task<Guid> ImportTransactionAsync(
        Guid accountId,
        Guid importBatchId,
        string description = "payment"
    )
    {
        var transactionId = Guid.CreateVersion7();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                accountId,
                new DateOnly(2026, 6, 15),
                null,
                -3.50m,
                "EUR",
                -3.50m,
                "Cafe",
                description,
                null,
                $"hash-{transactionId}",
                importBatchId,
                null
            )
        );
        await session.SaveChangesAsync(Ct);
        return transactionId;
    }

    private async Task LinkAsTransferAsync(Guid first, Guid second)
    {
        await using var session = fixture.Store.LightweightSession();
        var firstStream = await session.Events.FetchForWriting<TransactionView>(first, Ct);
        firstStream.AppendOne(new TransactionLinkedAsTransfer(second));
        var secondStream = await session.Events.FetchForWriting<TransactionView>(second, Ct);
        secondStream.AppendOne(new TransactionLinkedAsTransfer(first));
        await session.SaveChangesAsync(Ct);
    }

    private async Task StoreAsync(params object[] documents)
    {
        await using var session = fixture.Store.LightweightSession();
        session.Store(documents);
        await session.SaveChangesAsync(Ct);
    }

    #endregion
}
