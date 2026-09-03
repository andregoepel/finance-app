using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Transactions;

/// <summary>
/// Auto-linking exact pairs and queuing everything else for review, end to end
/// against a real Postgres — the projection read and the event append have to
/// land together for the "already linked" idempotency checks to mean anything.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchTransfersCommandHandlerTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_ExactPair_IsLinkedOnBothLegs()
    {
        // Arrange
        var (accountA, accountB) = await CreateAccountPairAsync();
        var outgoing = await ImportAsync(accountA, -100m, Today);
        var incoming = await ImportAsync(accountB, 100m, Today);

        // Act
        await MatchAsync();

        // Assert
        await using var session = fixture.Store.QuerySession();
        var outgoingView = await session.LoadAsync<TransactionView>(outgoing, Ct);
        var incomingView = await session.LoadAsync<TransactionView>(incoming, Ct);
        Assert.Equal(incoming, outgoingView!.TransferCounterpartId);
        Assert.Equal(outgoing, incomingView!.TransferCounterpartId);
        Assert.Empty(await session.Query<TransferSuggestion>().ToListAsync(Ct));
    }

    [Fact]
    public async Task Handle_FuzzyPair_BecomesAPendingSuggestion()
    {
        // Arrange — 3 days apart, 1 EUR off.
        var (accountA, accountB) = await CreateAccountPairAsync();
        var outgoing = await ImportAsync(accountA, -100m, Today);
        var incoming = await ImportAsync(accountB, 99m, Today.AddDays(3));

        // Act
        await MatchAsync();

        // Assert
        await using var session = fixture.Store.QuerySession();
        Assert.Null(
            (await session.LoadAsync<TransactionView>(outgoing, Ct))!.TransferCounterpartId
        );
        var suggestion = Assert.Single(await session.Query<TransferSuggestion>().ToListAsync(Ct));
        Assert.Equal(outgoing, suggestion.OutgoingTransactionId);
        Assert.Equal(incoming, suggestion.IncomingTransactionId);
        Assert.False(suggestion.Dismissed);
    }

    [Fact]
    public async Task Handle_RunTwice_IsIdempotent()
    {
        // Arrange
        var (accountA, accountB) = await CreateAccountPairAsync();
        await ImportAsync(accountA, -100m, Today);
        await ImportAsync(accountB, 100m, Today);
        await MatchAsync();

        // Act — nothing changed, run again.
        await MatchAsync();

        // Assert — still exactly one linked pair, no duplicate suggestions.
        await using var session = fixture.Store.QuerySession();
        var linked = await session
            .Query<TransactionView>()
            .Where(t => t.TransferCounterpartId != null)
            .ToListAsync(Ct);
        Assert.Equal(2, linked.Count);
        Assert.Empty(await session.Query<TransferSuggestion>().ToListAsync(Ct));
    }

    [Fact]
    public async Task Handle_DismissedSuggestion_IsNeverRecreated()
    {
        // Arrange
        var (accountA, accountB) = await CreateAccountPairAsync();
        var outgoing = await ImportAsync(accountA, -100m, Today);
        var incoming = await ImportAsync(accountB, 99m, Today.AddDays(3));
        await MatchAsync();
        var suggestion = Assert.Single(await QuerySuggestionsAsync());
        await DismissAsync(suggestion.Id);

        // Act — matcher runs again over the same, still-unlinked pair.
        await MatchAsync();

        // Assert
        var suggestions = await QuerySuggestionsAsync();
        var stillDismissed = Assert.Single(suggestions);
        Assert.True(stillDismissed.Dismissed);
        await using var session = fixture.Store.QuerySession();
        Assert.Null(
            (await session.LoadAsync<TransactionView>(outgoing, Ct))!.TransferCounterpartId
        );
        Assert.Null(
            (await session.LoadAsync<TransactionView>(incoming, Ct))!.TransferCounterpartId
        );
    }

    [Fact]
    public async Task Handle_AcceptedSuggestion_LinksAndClearsCompetingSuggestions()
    {
        // Arrange — one outgoing leg with two plausible incoming candidates.
        var (accountA, accountB) = await CreateAccountPairAsync();
        var outgoing = await ImportAsync(accountA, -100m, Today);
        var right = await ImportAsync(accountB, 99m, Today.AddDays(2));
        var wrong = await ImportAsync(accountB, 98.5m, Today.AddDays(4));
        await MatchAsync();
        var suggestions = await QuerySuggestionsAsync();
        Assert.Equal(2, suggestions.Count);
        var accepted = suggestions.Single(s => s.IncomingTransactionId == right);

        // Act
        var result = await AcceptAsync(accepted.Id);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        await using var session = fixture.Store.QuerySession();
        Assert.Equal(
            right,
            (await session.LoadAsync<TransactionView>(outgoing, Ct))!.TransferCounterpartId
        );
        Assert.Null((await session.LoadAsync<TransactionView>(wrong, Ct))!.TransferCounterpartId);
        var remaining = await session.Query<TransferSuggestion>().ToListAsync(Ct);
        Assert.All(remaining, s => Assert.True(s.Dismissed));
    }

    #region World

    private async Task MatchAsync()
    {
        await using var session = fixture.Store.LightweightSession();
        await MatchTransfersCommandHandler.Handle(
            new MatchTransfersCommand(),
            session,
            NullLogger<MatchTransfersCommand>.Instance,
            Ct
        );
    }

    private async Task<Result> AcceptAsync(string suggestionId)
    {
        await using var session = fixture.Store.LightweightSession();
        return await AcceptTransferSuggestionCommandHandler.Handle(
            new AcceptTransferSuggestionCommand(suggestionId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
    }

    private async Task DismissAsync(string suggestionId)
    {
        await using var session = fixture.Store.LightweightSession();
        var result = await DismissTransferSuggestionCommandHandler.Handle(
            new DismissTransferSuggestionCommand(suggestionId),
            session,
            DomainLocalizer.Instance,
            Ct
        );
        Assert.True(result.IsSuccess, result.Error);
    }

    private async Task<List<TransferSuggestion>> QuerySuggestionsAsync()
    {
        await using var session = fixture.Store.QuerySession();
        return (await session.Query<TransferSuggestion>().ToListAsync(Ct)).ToList();
    }

    private async Task<(Guid AccountA, Guid AccountB)> CreateAccountPairAsync()
    {
        var a = new Account
        {
            Name = "Wise",
            Provider = ProviderKind.Wise,
            Type = AccountType.Checking,
            Currency = "EUR",
            SyncMethod = SyncMethod.Api,
        };
        var b = new Account
        {
            Name = "DKB",
            Provider = ProviderKind.Dkb,
            Type = AccountType.Checking,
            Currency = "EUR",
            SyncMethod = SyncMethod.CsvUpload,
        };
        await using var session = fixture.Store.LightweightSession();
        session.StoreObjects([a, b]);
        await session.SaveChangesAsync(Ct);
        return (a.Id, b.Id);
    }

    private async Task<Guid> ImportAsync(Guid accountId, decimal amount, DateOnly bookingDate)
    {
        var batch = new ImportBatch
        {
            AccountId = accountId,
            Provider = ProviderKind.Dkb,
            Source = "statement.csv",
            ParserId = "dkb-csv-v1",
        };
        var transactionId = Guid.CreateVersion7();
        await using var session = fixture.Store.LightweightSession();
        session.Store(batch);
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                accountId,
                bookingDate,
                null,
                amount,
                "EUR",
                amount,
                "Counterparty",
                "transfer",
                null,
                $"hash-{transactionId}",
                batch.Id,
                null
            )
        );
        await session.SaveChangesAsync(Ct);
        return transactionId;
    }

    #endregion
}
