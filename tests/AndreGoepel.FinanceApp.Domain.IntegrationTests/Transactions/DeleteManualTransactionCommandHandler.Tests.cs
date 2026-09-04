using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

/// <summary>
/// Taking back a hand-entered transaction is the per-row counterpart of clearing
/// an account: the stream, its projection and its one-row batch go, the ledger
/// balance moves back, and nothing on other accounts is touched. Imported rows
/// are refused. Runs against a real Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeleteManualTransactionCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_UnknownTransaction_Fails()
    {
        // Act
        var result = await DeleteAsync(Guid.NewGuid());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Transaction not found.", result.Error);
    }

    [Fact]
    public async Task Handle_ImportedTransaction_Fails()
    {
        // Arrange
        var accountId = await CreateAccountAsync(SyncMethod.CsvUpload);
        var batchId = await CreateImportBatchAsync(accountId, "dkb-csv-v1");
        var transactionId = await ImportTransactionAsync(accountId, batchId);

        // Act
        var result = await DeleteAsync(transactionId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "Only hand-entered transactions can be deleted; imported history stays.",
            result.Error
        );
        await using var session = fixture.Store.QuerySession();
        Assert.NotNull(await session.LoadAsync<TransactionView>(transactionId, Ct));
    }

    [Fact]
    public async Task Handle_RemovesStreamProjectionAndBatch()
    {
        // Arrange
        var accountId = await CreateAccountAsync();
        var recorded = await RecordAsync(accountId, -12.5m);

        // Act
        var result = await DeleteAsync(recorded.Id);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<TransactionView>(recorded.Id, Ct));
        Assert.Empty(await session.Events.FetchStreamAsync(recorded.Id, token: Ct));
        Assert.Null(await session.LoadAsync<ImportBatch>(recorded.ImportBatchId, Ct));
    }

    [Fact]
    public async Task Handle_MovesTheLedgerBalanceBack()
    {
        // Arrange — opening 50, expense 12.50 recorded, then taken back.
        var accountId = await CreateAccountAsync(balance: 50m);
        var recorded = await RecordAsync(accountId, -12.5m);

        // Act
        await DeleteAsync(recorded.Id);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var account = await session.LoadAsync<Account>(accountId, Ct);
        Assert.Equal(50m, account!.CurrentBalance);
        Assert.Equal(50m, account.CurrentBalanceEur);
    }

    [Fact]
    public async Task Handle_OtherEntries_StayUntouched()
    {
        // Arrange
        var accountId = await CreateAccountAsync();
        var kept = await RecordAsync(accountId, -3m);
        var removed = await RecordAsync(accountId, -4m);

        // Act
        await DeleteAsync(removed.Id);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var remaining = await session.Query<TransactionView>().ToListAsync(Ct);
        Assert.Equal(kept.Id, Assert.Single(remaining).Id);
        Assert.NotNull(await session.LoadAsync<ImportBatch>(kept.ImportBatchId, Ct));
        var account = await session.LoadAsync<Account>(accountId, Ct);
        Assert.Equal(-3m, account!.CurrentBalance);
    }

    [Fact]
    public async Task Handle_TransferCounterpart_IsUnlinkedNotDeleted()
    {
        // Arrange — a cash withdrawal linked to its bank leg.
        var cashAccountId = await CreateAccountAsync();
        var bankAccountId = await CreateAccountAsync(SyncMethod.CsvUpload);
        var bankBatchId = await CreateImportBatchAsync(bankAccountId, "dkb-csv-v1");
        var withdrawal = await RecordAsync(cashAccountId, 100m);
        var bankLeg = await ImportTransactionAsync(bankAccountId, bankBatchId);
        await LinkAsTransferAsync(withdrawal.Id, bankLeg);

        // Act
        var result = await DeleteAsync(withdrawal.Id);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var counterpart = await session.LoadAsync<TransactionView>(bankLeg, Ct);
        Assert.NotNull(counterpart);
        Assert.Null(counterpart.TransferCounterpartId);
    }

    [Fact]
    public async Task Handle_ReviewQueueEntry_GoesWithTheTransaction()
    {
        // Arrange
        var accountId = await CreateAccountAsync();
        var recorded = await RecordAsync(accountId, -12.5m);
        await StoreAsync(
            new CategorySuggestion
            {
                Id = recorded.Id,
                CategoryId = Guid.NewGuid(),
                Confidence = 0.4m,
            }
        );

        // Act
        await DeleteAsync(recorded.Id);

        // Assert
        await using var session = fixture.Store.QuerySession();
        Assert.Null(await session.LoadAsync<CategorySuggestion>(recorded.Id, Ct));
    }

    #region World

    private async Task<Result> DeleteAsync(Guid transactionId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await DeleteManualTransactionCommandHandler.Handle(
            new DeleteManualTransactionCommand(transactionId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task<TransactionView> RecordAsync(Guid accountId, decimal amount)
    {
        await using var session = fixture.Store.LightweightSession();
        var result = await RecordManualTransactionCommandHandler.Handle(
            new RecordManualTransactionCommand(
                accountId,
                new DateOnly(2026, 9, 3),
                amount,
                AmountEur: null,
                Description: "cash entry",
                Counterparty: null,
                CategoryId: null,
                RecordedBy: "andre"
            ),
            session,
            DomainLocalizer.Instance,
            Ct
        );
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private async Task<Guid> CreateAccountAsync(
        SyncMethod syncMethod = SyncMethod.Manual,
        decimal? balance = null
    )
    {
        var account = new Account
        {
            Name = syncMethod == SyncMethod.Manual ? "Cash" : "Bank",
            Provider = syncMethod == SyncMethod.Manual ? ProviderKind.Cash : ProviderKind.Dkb,
            Type = syncMethod == SyncMethod.Manual ? AccountType.Cash : AccountType.Checking,
            Currency = "EUR",
            SyncMethod = syncMethod,
            CurrentBalance = balance,
            CurrentBalanceEur = balance,
            BalanceUpdatedAt = balance is null ? null : DateTimeOffset.UtcNow,
        };
        await StoreAsync(account);
        return account.Id;
    }

    private async Task<Guid> CreateImportBatchAsync(Guid accountId, string parserId)
    {
        var batch = new ImportBatch
        {
            AccountId = accountId,
            Provider = ProviderKind.Dkb,
            Source = "statement.csv",
            ParserId = parserId,
        };
        await StoreAsync(batch);
        return batch.Id;
    }

    private async Task<Guid> ImportTransactionAsync(Guid accountId, Guid importBatchId)
    {
        var transactionId = Guid.CreateVersion7();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                accountId,
                new DateOnly(2026, 9, 3),
                null,
                -100m,
                "EUR",
                -100m,
                "ATM",
                "cash withdrawal",
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
        session.StoreObjects(documents);
        await session.SaveChangesAsync(Ct);
    }

    #endregion
}
