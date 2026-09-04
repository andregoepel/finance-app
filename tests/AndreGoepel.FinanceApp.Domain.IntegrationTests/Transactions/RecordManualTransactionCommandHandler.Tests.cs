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
/// A hand-entered cash transaction must look exactly like an imported one to the
/// rest of the app — stream, projection, batch — and must move the account's
/// ledger balance. Runs against a real Postgres because the inline projection and
/// the anchor update land in one unit of work.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RecordManualTransactionCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_UnknownAccount_Fails()
    {
        // Act
        var result = await RecordAsync(Command(Guid.NewGuid()));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Account not found.", result.Error);
    }

    [Fact]
    public async Task Handle_ImportedAccount_Fails()
    {
        // Arrange — hand entry would corrupt the dedup contract of a synced account.
        var accountId = await CreateAccountAsync(SyncMethod.CsvUpload);

        // Act
        var result = await RecordAsync(Command(accountId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "Transactions can only be entered by hand on manually maintained accounts (cash).",
            result.Error
        );
    }

    [Fact]
    public async Task Handle_ZeroAmount_Fails()
    {
        // Arrange
        var accountId = await CreateAccountAsync();

        // Act
        var result = await RecordAsync(Command(accountId) with { Amount = 0m });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "The amount must not be zero (negative = expense, positive = income).",
            result.Error
        );
    }

    [Fact]
    public async Task Handle_BlankDescription_Fails()
    {
        // Arrange
        var accountId = await CreateAccountAsync();

        // Act
        var result = await RecordAsync(Command(accountId) with { Description = "  " });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("A description is required.", result.Error);
    }

    [Fact]
    public async Task Handle_UnknownCategory_Fails()
    {
        // Arrange
        var accountId = await CreateAccountAsync();

        // Act
        var result = await RecordAsync(Command(accountId) with { CategoryId = Guid.NewGuid() });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category not found.", result.Error);
    }

    [Fact]
    public async Task Handle_RecordsStreamProjectionAndAuditBatch()
    {
        // Arrange
        var accountId = await CreateAccountAsync();

        // Act
        var result = await RecordAsync(Command(accountId));

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var view = result.Value!;
        Assert.Equal(accountId, view.AccountId);
        Assert.Equal(Today, view.BookingDate);
        Assert.Equal(-12.5m, view.Amount);
        Assert.Equal("EUR", view.Currency);
        Assert.Equal(-12.5m, view.AmountEur);
        Assert.Equal("Bakery", view.Counterparty);
        Assert.Equal("bread and rolls", view.Description);
        Assert.Null(view.CategoryId);

        await using var session = fixture.Store.QuerySession();
        Assert.NotEmpty(await session.Events.FetchStreamAsync(view.Id, token: Ct));
        var batch = await session.LoadAsync<ImportBatch>(view.ImportBatchId, Ct);
        Assert.NotNull(batch);
        Assert.Equal(RecordManualTransactionCommandHandler.ParserId, batch.ParserId);
        Assert.Equal(ProviderKind.Cash, batch.Provider);
        Assert.Equal("andre", batch.ImportedBy);
        Assert.Equal(1, batch.ImportedCount);
        Assert.Equal(1, batch.TotalRows);
    }

    [Fact]
    public async Task Handle_WithCategory_AppliesItAsManual()
    {
        // Arrange
        var accountId = await CreateAccountAsync();
        var category = new Category { Name = "Groceries" };
        await StoreAsync(category);

        // Act
        var result = await RecordAsync(Command(accountId) with { CategoryId = category.Id });

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(category.Id, result.Value!.CategoryId);
        Assert.Equal(CategorySource.Manual, result.Value.CategorySource);
    }

    [Fact]
    public async Task Handle_MovesTheLedgerBalance()
    {
        // Arrange — opening balance 50, one expense of 12.50.
        var accountId = await CreateAccountAsync(balance: 50m);

        // Act
        var result = await RecordAsync(Command(accountId));

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        var account = await session.LoadAsync<Account>(accountId, Ct);
        Assert.NotNull(account);
        Assert.Equal(37.5m, account.CurrentBalance);
        Assert.Equal(37.5m, account.CurrentBalanceEur);
        Assert.NotNull(account.BalanceUpdatedAt);
    }

    [Fact]
    public async Task Handle_ForeignCurrency_UsesTheCallerEurValue()
    {
        // Arrange
        var accountId = await CreateAccountAsync(currency: "CHF", balance: 100m, balanceEur: 104m);

        // Act
        var result = await RecordAsync(
            Command(accountId) with
            {
                Amount = -10m,
                AmountEur = -10.4m,
            }
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("CHF", result.Value!.Currency);
        Assert.Equal(-10.4m, result.Value.AmountEur);
        await using var session = fixture.Store.QuerySession();
        var account = await session.LoadAsync<Account>(accountId, Ct);
        Assert.Equal(90m, account!.CurrentBalance);
        Assert.Equal(93.6m, account.CurrentBalanceEur);
    }

    [Fact]
    public async Task Handle_SameEntryTwice_RecordsBoth()
    {
        // Arrange — two coffees on one day are two transactions; hand entry never dedups.
        var accountId = await CreateAccountAsync();

        // Act
        var first = await RecordAsync(Command(accountId));
        var second = await RecordAsync(Command(accountId));

        // Assert
        Assert.True(first.IsSuccess && second.IsSuccess);
        await using var session = fixture.Store.QuerySession();
        Assert.Equal(2, await session.Query<TransactionView>().CountAsync(Ct));
        var account = await session.LoadAsync<Account>(accountId, Ct);
        Assert.Equal(-25m, account!.CurrentBalance);
    }

    #region World

    private static RecordManualTransactionCommand Command(Guid accountId) =>
        new(
            accountId,
            Today,
            Amount: -12.5m,
            AmountEur: null,
            Description: "bread and rolls",
            Counterparty: "Bakery",
            CategoryId: null,
            RecordedBy: "andre"
        );

    private async Task<Result<TransactionView>> RecordAsync(RecordManualTransactionCommand command)
    {
        await using var session = fixture.Store.LightweightSession();
        return await RecordManualTransactionCommandHandler.Handle(
            command,
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task<Guid> CreateAccountAsync(
        SyncMethod syncMethod = SyncMethod.Manual,
        string currency = "EUR",
        decimal? balance = null,
        decimal? balanceEur = null
    )
    {
        var account = new Account
        {
            Name = "Cash",
            Provider = syncMethod == SyncMethod.Manual ? ProviderKind.Cash : ProviderKind.Dkb,
            Type = syncMethod == SyncMethod.Manual ? AccountType.Cash : AccountType.Checking,
            Currency = currency,
            SyncMethod = syncMethod,
            CurrentBalance = balance,
            CurrentBalanceEur = balanceEur ?? (currency == "EUR" ? balance : null),
            BalanceUpdatedAt = balance is null ? null : DateTimeOffset.UtcNow,
        };
        await StoreAsync(account);
        return account.Id;
    }

    private async Task StoreAsync(params object[] documents)
    {
        await using var session = fixture.Store.LightweightSession();
        session.StoreObjects(documents);
        await session.SaveChangesAsync(Ct);
    }

    #endregion
}
