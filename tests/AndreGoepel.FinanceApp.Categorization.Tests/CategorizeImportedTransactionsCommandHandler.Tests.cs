using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IntegrationCollection = AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Categorization.Tests;

/// <summary>
/// The backfill entry point of the categorization pipeline against a real
/// Postgres: which transactions it picks up, and that the shared pipeline turns
/// Claude's answers into events and review-queue suggestions. Claude itself is
/// a substitute — no network.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CategorizeImportedTransactionsCommandHandlerTests(
    CategorizationMartenFixture fixture
) : IAsyncLifetime
{
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync(Ct);
        await StoreAsync(
            new Category { Id = Groceries, Name = "Groceries" },
            new Category { Id = Rent, Name = "Rent" }
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_Backfill_SendsOnlyEligibleTransactionsToClaude()
    {
        // Arrange — one of each kind that must be skipped, two that must go out.
        var categorized = await ImportAsync("Rewe");
        await CategorizeManuallyAsync(categorized);
        var transferLeg = await ImportAsync("Own transfer");
        var counterpart = await ImportAsync("Own transfer");
        await LinkAsTransferAsync(transferLeg, counterpart);
        var awaitingReview = await ImportAsync("Amazon");
        await StoreAsync(
            new CategorySuggestion
            {
                Id = awaitingReview,
                CategoryId = Groceries,
                Confidence = 0.4m,
            }
        );
        var first = await ImportAsync("Billa");
        var second = await ImportAsync("Spar");

        var (categorizer, sent) = CategorizerAnswering([]);

        // Act
        await BackfillAsync(categorizer);

        // Assert
        Assert.Equal(new HashSet<Guid> { first, second }, sent.ToHashSet());
    }

    [Fact]
    public async Task Handle_Backfill_NothingEligible_NeverCallsClaude()
    {
        // Arrange
        var categorized = await ImportAsync("Rewe");
        await CategorizeManuallyAsync(categorized);
        var (categorizer, _) = CategorizerAnswering([]);

        // Act
        await BackfillAsync(categorizer);

        // Assert
        await categorizer.DidNotReceiveWithAnyArgs().SuggestAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Handle_Backfill_MatchingRule_CategorizesWithoutClaude()
    {
        // Arrange
        var transactionId = await ImportAsync("Billa");
        await StoreAsync(
            new CategoryRule
            {
                CategoryId = Groceries,
                CounterpartyContains = "Billa",
                Source = CategoryRuleSource.Manual,
            }
        );
        var (categorizer, _) = CategorizerAnswering([]);

        // Act
        await BackfillAsync(categorizer);

        // Assert
        var view = await LoadAsync(transactionId);
        Assert.Equal(Groceries, view.CategoryId);
        Assert.Equal(CategorySource.Rule, view.CategorySource);
        await categorizer.DidNotReceiveWithAnyArgs().SuggestAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Handle_Backfill_AppliesHighConfidenceAndQueuesLowConfidence()
    {
        // Arrange
        var confident = await ImportAsync("Billa");
        var unsure = await ImportAsync("Something");
        var declined = await ImportAsync("Unknown");
        var (categorizer, _) = CategorizerAnswering([
            new ClaudeCategorySuggestion(confident, Groceries, 0.95m),
            new ClaudeCategorySuggestion(unsure, Rent, 0.5m),
            new ClaudeCategorySuggestion(declined, null, 0.1m),
        ]);

        // Act
        await BackfillAsync(categorizer);

        // Assert
        var confidentView = await LoadAsync(confident);
        Assert.Equal(Groceries, confidentView.CategoryId);
        Assert.Equal(CategorySource.Ai, confidentView.CategorySource);
        Assert.Equal(0.95m, confidentView.CategoryConfidence);

        var unsureView = await LoadAsync(unsure);
        Assert.Null(unsureView.CategoryId);
        await using var session = fixture.Store.QuerySession();
        var suggestion = await session.LoadAsync<CategorySuggestion>(unsure, Ct);
        Assert.NotNull(suggestion);
        Assert.Equal(Rent, suggestion.CategoryId);

        Assert.Null((await LoadAsync(declined)).CategoryId);
        Assert.Null(await session.LoadAsync<CategorySuggestion>(declined, Ct));
    }

    [Fact]
    public async Task Handle_Backfill_ClaudeFailure_LeavesTransactionsUncategorized()
    {
        // Arrange
        var transactionId = await ImportAsync("Billa");
        var categorizer = Substitute.For<IClaudeCategorizer>();
        categorizer
            .SuggestAsync(default!, default!, default!, Ct)
            .ReturnsForAnyArgs(
                Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>("API unreachable")
            );

        // Act
        await BackfillAsync(categorizer);

        // Assert
        Assert.Null((await LoadAsync(transactionId)).CategoryId);
    }

    [Fact]
    public async Task Handle_ImportBatch_IgnoresOtherBatches()
    {
        // Arrange — the per-import entry point stays scoped to its batch.
        var batchId = Guid.NewGuid();
        var inBatch = await ImportAsync("Billa", batchId);
        await ImportAsync("Spar", Guid.NewGuid());
        var (categorizer, sent) = CategorizerAnswering([]);

        // Act
        await using var session = fixture.Store.LightweightSession();
        await new CategorizeImportedTransactionsCommandHandler().Handle(
            new CategorizeImportedTransactionsCommand(batchId),
            session,
            categorizer,
            NullLogger<CategorizeImportedTransactionsCommandHandler>.Instance,
            Ct
        );

        // Assert
        Assert.Equal(inBatch, Assert.Single(sent));
    }

    #region Helpers

    private async Task BackfillAsync(IClaudeCategorizer categorizer)
    {
        await using var session = fixture.Store.LightweightSession();
        await new CategorizeImportedTransactionsCommandHandler().Handle(
            new CategorizeUncategorizedTransactionsCommand(),
            session,
            categorizer,
            NullLogger<CategorizeImportedTransactionsCommandHandler>.Instance,
            Ct
        );
    }

    /// <summary>
    /// A Claude substitute that records every transaction id it is asked about
    /// and answers with the given suggestions.
    /// </summary>
    private static (IClaudeCategorizer Categorizer, List<Guid> Sent) CategorizerAnswering(
        IReadOnlyList<ClaudeCategorySuggestion> answer
    )
    {
        var sent = new List<Guid>();
        var categorizer = Substitute.For<IClaudeCategorizer>();
        categorizer
            .SuggestAsync(
                Arg.Do<IReadOnlyList<TransactionToCategorize>>(batch =>
                    sent.AddRange(batch.Select(t => t.TransactionId))
                ),
                Arg.Any<IReadOnlyList<CategoryOption>>(),
                Arg.Any<IReadOnlyList<FewShotExample>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Ok(answer));
        return (categorizer, sent);
    }

    private async Task<TransactionView> LoadAsync(Guid transactionId)
    {
        await using var session = fixture.Store.QuerySession();
        var view = await session.LoadAsync<TransactionView>(transactionId, Ct);
        Assert.NotNull(view);
        return view;
    }

    private async Task<Guid> ImportAsync(string counterparty, Guid? importBatchId = null)
    {
        var transactionId = Guid.CreateVersion7();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                Guid.NewGuid(),
                new DateOnly(2026, 6, 15),
                null,
                -12.30m,
                "EUR",
                -12.30m,
                counterparty,
                "card payment",
                null,
                $"hash-{transactionId}",
                importBatchId ?? Guid.NewGuid(),
                null
            )
        );
        await session.SaveChangesAsync(Ct);
        return transactionId;
    }

    private async Task CategorizeManuallyAsync(Guid transactionId)
    {
        await using var session = fixture.Store.LightweightSession();
        var stream = await session.Events.FetchForWriting<TransactionView>(transactionId, Ct);
        stream.AppendOne(new TransactionCategorized(Groceries, CategorySource.Manual, null));
        await session.SaveChangesAsync(Ct);
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
