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
/// The hard delete is a cascade across event streams, projections and five
/// document types committed as one transaction — exactly the behaviour a mocked
/// session cannot show. Every test runs against a real Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HardDeleteAccountCommandHandlerTests(FinanceMartenFixture fixture)
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
        var result = await HardDeleteAccountCommandHandler.Handle(
            new HardDeleteAccountCommand(Guid.NewGuid()),
            session,
            DomainLocalizer.Instance,
            Ct
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Account not found.", result.Error);
    }

    [Fact]
    public async Task Handle_AccountWithoutHistory_DeletesOnlyTheAccount()
    {
        // Arrange
        var accountId = await CreateAccountAsync("Empty");

        // Act
        var impact = await HardDeleteAsync(accountId);

        // Assert
        Assert.True(impact.IsAccountOnly);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<Account>(accountId, Ct));
    }

    [Fact]
    public async Task DeleteAccountCommand_AccountWithTransactions_StillRefuses()
    {
        // Arrange
        var accountId = await CreateAccountAsync("Wise");
        var batchId = await CreateImportBatchAsync(accountId);
        await ImportTransactionAsync(accountId, batchId);

        // Act
        await using var session = fixture.Store.LightweightSession();
        var result = await DeleteAccountCommandHandler.Handle(
            new DeleteAccountCommand(accountId),
            session,
            DomainLocalizer.Instance,
            Ct
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("deactivate it instead", result.Error!);
        await using var query = fixture.Store.QuerySession();
        Assert.NotNull(await query.LoadAsync<Account>(accountId, Ct));
    }

    [Fact]
    public async Task Handle_AccountWithTransactions_DeletesStreamsAndProjections()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        await HardDeleteAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<Account>(world.AccountId, Ct));
        foreach (var transactionId in world.TransactionIds)
        {
            Assert.Null(await session.LoadAsync<TransactionView>(transactionId, Ct));
            Assert.Empty(await session.Events.FetchStreamAsync(transactionId, token: Ct));
        }
    }

    [Fact]
    public async Task Handle_AccountWithTransactions_DeletesImportBatchesOfThatAccountOnly()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        await HardDeleteAsync(world.AccountId);

        // Assert
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
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.TransfersUnlinked);
        await using var session = fixture.Store.QuerySession();
        var counterpart = await session.LoadAsync<TransactionView>(world.CounterpartId, Ct);
        Assert.NotNull(counterpart);
        Assert.Null(counterpart.TransferCounterpartId);
        Assert.NotEmpty(await session.Events.FetchStreamAsync(world.CounterpartId, token: Ct));
        Assert.Empty(
            await session
                .Query<TransactionView>()
                .Where(t => t.TransferCounterpartId != null)
                .ToListAsync(Ct)
        );
    }

    [Fact]
    public async Task Handle_PlannedMatches_AreClearedForDeletedTransactionsOnly()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.PlannedMatchesCleared);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<PlannedMatch>(world.PlannedMatchId, Ct));
        Assert.NotNull(await session.LoadAsync<PlannedMatch>(world.OtherPlannedMatchId, Ct));
    }

    [Fact]
    public async Task Handle_ReviewQueueEntries_AreRemovedForDeletedTransactionsOnly()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.ReviewQueueEntries);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<CategorySuggestion>(world.GroceriesId, Ct));
        Assert.NotNull(await session.LoadAsync<CategorySuggestion>(world.CounterpartId, Ct));
    }

    [Fact]
    public async Task Handle_CryptoHoldings_AreRemovedForThatAccountOnly()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.CryptoHoldings);
        await using var session = fixture.Store.QuerySession();
        var holdings = await session.Query<CryptoHolding>().ToListAsync(Ct);
        Assert.Equal(world.OtherAccountId, Assert.Single(holdings).AccountId);
    }

    [Fact]
    public async Task Handle_PlannedItemExpectingTheAccount_IsDetachedNotDeleted()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.Equal(1, impact.PlannedItemsDetached);
        await using var session = fixture.Store.QuerySession();
        var item = await session.LoadAsync<PlannedItem>(world.PlannedItemId, Ct);
        Assert.NotNull(item);
        Assert.Null(item.ExpectedAccountId);
    }

    [Fact]
    public async Task Handle_LearnedCategoryRules_Survive()
    {
        // Arrange
        var world = await CreateWorldAsync();

        // Act
        await HardDeleteAsync(world.AccountId);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var rule = await session.LoadAsync<CategoryRule>(world.CategoryRuleId, Ct);
        Assert.NotNull(rule);
        Assert.Equal(CategoryRuleSource.LearnedFromCorrection, rule.Source);
    }

    [Fact]
    public async Task Preview_MatchesWhatTheDeleteReports()
    {
        // Arrange
        var world = await CreateWorldAsync();
        await using var previewSession = fixture.Store.QuerySession();
        var preview = await AccountDeletionPreview.ForAccountAsync(
            previewSession,
            world.AccountId,
            DomainLocalizer.Instance,
            Ct
        );

        // Act
        var impact = await HardDeleteAsync(world.AccountId);

        // Assert
        Assert.True(preview.IsSuccess);
        Assert.Equal(preview.Value, impact);
        Assert.Equal(2, impact.Transactions);
        Assert.Equal(1, impact.ImportBatches);
    }

    [Fact]
    public async Task Preview_UnknownAccount_Fails()
    {
        // Act
        await using var session = fixture.Store.QuerySession();
        var result = await AccountDeletionPreview.ForAccountAsync(
            session,
            Guid.NewGuid(),
            DomainLocalizer.Instance,
            Ct
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    #region World

    private async Task<AccountDeletionImpact> HardDeleteAsync(Guid accountId)
    {
        await using var session = fixture.Store.LightweightSession();
        var result = await HardDeleteAccountCommandHandler.Handle(
            new HardDeleteAccountCommand(accountId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    /// <summary>
    /// Two accounts, so every assertion can also show that the untouched
    /// account's data survives: the deleted one owns two transactions (one of
    /// them a transfer leg), an import batch, a crypto holding, a planned match
    /// and a review-queue entry; the other owns the transfer counterpart and a
    /// copy of each of those.
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
            otherAccountId,
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
        Guid OtherAccountId,
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
        var dueDate = new DateOnly(2026, month, 1);
        var match = new PlannedMatch
        {
            Id = PlannedMatch.KeyFor(plannedItemId, dueDate, transactionId),
            PlannedItemId = plannedItemId,
            DueDate = dueDate,
            TransactionId = transactionId,
        };
        await StoreAsync(match);
        return match.Id;
    }

    private async Task<Guid> ImportTransactionAsync(
        Guid accountId,
        Guid importBatchId,
        string description = "coffee"
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
        var result = await LinkTransactionsAsTransferCommandHandler.Handle(
            new LinkTransactionsAsTransferCommand(first, second),
            session,
            DomainLocalizer.Instance,
            Ct
        );
        Assert.True(result.IsSuccess, result.Error);
    }

    private async Task StoreAsync(params object[] documents)
    {
        await using var session = fixture.Store.LightweightSession();
        session.StoreObjects(documents);
        await session.SaveChangesAsync(Ct);
    }

    #endregion
}
