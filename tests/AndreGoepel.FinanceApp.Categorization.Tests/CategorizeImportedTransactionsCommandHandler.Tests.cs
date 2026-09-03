using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;
using IntegrationCollection = AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Categorization.Tests;

/// <summary>
/// The categorization pipeline against a real Postgres: which transactions
/// each stage picks up (rules, household history, Claude) and that Claude's
/// answers turn into events and review-queue suggestions. Claude itself is a
/// substitute — no network.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CategorizeImportedTransactionsCommandHandlerTests(
    CategorizationMartenFixture fixture
) : IAsyncLifetime
{
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly Guid Insurance = Guid.NewGuid();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync(Ct);
        await StoreAsync(
            new Category { Id = Groceries, Name = "Groceries" },
            new Category { Id = Rent, Name = "Rent" },
            new Category { Id = Insurance, Name = "Health insurance" }
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Backfill scope

    [Fact]
    public async Task Handle_Backfill_SendsOnlyEligibleTransactionsToClaude()
    {
        // Arrange — one of each kind that must be skipped, two that must go out.
        var categorized = await ImportAsync("Rewe");
        await CategorizeAsync(categorized, Groceries, CategorySource.Manual);
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

        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        Assert.Equal(new HashSet<Guid> { first, second }, claude.SentIds.ToHashSet());
    }

    [Fact]
    public async Task Handle_Backfill_NothingEligible_NeverCallsClaude()
    {
        // Arrange
        var categorized = await ImportAsync("Rewe");
        await CategorizeAsync(categorized, Groceries, CategorySource.Manual);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        await claude
            .Categorizer.DidNotReceiveWithAnyArgs()
            .SuggestAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Handle_ImportBatch_IgnoresOtherBatches()
    {
        // Arrange — the per-import entry point stays scoped to its batch.
        var batchId = Guid.NewGuid();
        var inBatch = await ImportAsync("Billa", importBatchId: batchId);
        await ImportAsync("Spar", importBatchId: Guid.NewGuid());
        var claude = ClaudeAnswering([]);

        // Act
        await ImportFollowUpAsync(batchId, claude.Categorizer);

        // Assert
        Assert.Equal(inBatch, Assert.Single(claude.SentIds));
    }

    [Fact]
    public async Task Handle_Backfill_CascadesOneMessagePerClaudeBatch()
    {
        // Arrange — one more than a batch: two messages, the second carrying a single id.
        var ids = new List<Guid>();
        for (var i = 0; i <= CategorizeImportedTransactionsCommandHandler.BatchSize; i++)
        {
            ids.Add(await ImportAsync($"Shop {i}"));
        }

        // Act
        var batches = await PlanBackfillAsync();

        // Assert
        var commands = batches.OfType<CategorizeTransactionBatchCommand>().ToList();
        Assert.Equal(2, commands.Count);
        Assert.Equal(
            CategorizeImportedTransactionsCommandHandler.BatchSize,
            commands[0].TransactionIds.Count
        );
        Assert.Single(commands[1].TransactionIds);
        Assert.Equal(ids.ToHashSet(), commands.SelectMany(c => c.TransactionIds).ToHashSet());
        Assert.All(commands, command => Assert.Equal("backfill", command.Scope));
    }

    [Fact]
    public async Task Handle_Batch_SkipsRowsCategorizedWhileItWaitedInTheQueue()
    {
        // Arrange — a manual pick lands between planning the batches and the Claude call.
        var picked = await ImportAsync("Billa");
        var open = await ImportAsync("Spar");
        var batches = await PlanBackfillAsync();
        await CategorizeAsync(picked, Groceries, CategorySource.Manual);
        var claude = ClaudeAnswering([]);

        // Act
        await RunBatchesAsync(batches, claude.Categorizer);

        // Assert
        Assert.Equal(open, Assert.Single(claude.SentIds));
    }

    #endregion

    #region Stage 1: rules

    [Fact]
    public async Task Handle_MatchingRule_CategorizesWithoutClaude()
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
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        var view = await LoadAsync(transactionId);
        Assert.Equal(Groceries, view.CategoryId);
        Assert.Equal(CategorySource.Rule, view.CategorySource);
        await claude
            .Categorizer.DidNotReceiveWithAnyArgs()
            .SuggestAsync(default!, default!, default!, Ct);
    }

    #endregion

    #region Stage 2: household history

    [Fact]
    public async Task Handle_CounterpartyConfirmedTwice_CategorizesFromHistoryWithoutClaude()
    {
        // Arrange — the monthly insurance premium, confirmed by hand twice.
        var april = await ImportAsync("UNIQA", month: 4, amount: -142.50m);
        var may = await ImportAsync("UNIQA", month: 5, amount: -142.50m);
        await CategorizeAsync(april, Insurance, CategorySource.Manual);
        await CategorizeAsync(may, Insurance, CategorySource.Manual);
        var june = await ImportAsync("UNIQA", month: 6, amount: -142.50m);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        var view = await LoadAsync(june);
        Assert.Equal(Insurance, view.CategoryId);
        Assert.Equal(CategorySource.History, view.CategorySource);
        Assert.Null(view.CategoryConfidence);
        await claude
            .Categorizer.DidNotReceiveWithAnyArgs()
            .SuggestAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Handle_CounterpartyConfirmedOnce_StillAsksClaude()
    {
        // Arrange
        var april = await ImportAsync("UNIQA", month: 4);
        await CategorizeAsync(april, Insurance, CategorySource.Manual);
        var june = await ImportAsync("UNIQA", month: 6);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        Assert.Equal(june, Assert.Single(claude.SentIds));
    }

    [Fact]
    public async Task Handle_AiCategorizationsDoNotCountAsConfirmedHistory()
    {
        // Arrange — two earlier AI guesses must not turn into "history".
        var april = await ImportAsync("UNIQA", month: 4);
        var may = await ImportAsync("UNIQA", month: 5);
        await CategorizeAsync(april, Insurance, CategorySource.Ai, 0.9m);
        await CategorizeAsync(may, Insurance, CategorySource.Ai, 0.9m);
        var june = await ImportAsync("UNIQA", month: 6);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        Assert.Equal(june, Assert.Single(claude.SentIds));
    }

    [Fact]
    public async Task Handle_ConflictingHistory_LeavesTheDecisionToClaude()
    {
        // Arrange
        var april = await ImportAsync("Amazon", month: 4);
        var may = await ImportAsync("Amazon", month: 5);
        await CategorizeAsync(april, Groceries, CategorySource.Manual);
        await CategorizeAsync(may, Rent, CategorySource.Manual);
        var june = await ImportAsync("Amazon", month: 6);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        Assert.Equal(june, Assert.Single(claude.SentIds));
    }

    #endregion

    #region Stage 3: what Claude gets

    [Fact]
    public async Task Handle_PassesBookingDateRecurrenceAndCounterpartyExamplesToClaude()
    {
        // Arrange — a monthly series confirmed only once (so Claude is asked),
        // plus an unrelated recent confirmation that should be there as fill.
        var march = await ImportAsync("Netflix", month: 3, amount: -15.99m);
        var april = await ImportAsync("Netflix", month: 4, amount: -15.99m);
        var may = await ImportAsync("Netflix", month: 5, amount: -15.99m);
        await CategorizeAsync(march, Rent, CategorySource.Ai, 0.9m);
        await CategorizeAsync(april, Rent, CategorySource.Ai, 0.9m);
        await CategorizeAsync(may, Rent, CategorySource.Manual);
        var billa = await ImportAsync("Billa", month: 5, day: 2);
        await CategorizeAsync(billa, Groceries, CategorySource.Manual);
        var june = await ImportAsync("Netflix", month: 6, amount: -15.99m);
        var claude = ClaudeAnswering([]);

        // Act
        await BackfillAsync(claude.Categorizer);

        // Assert
        var sent = Assert.Single(claude.SentTransactions);
        Assert.Equal(june, sent.TransactionId);
        Assert.Equal(new DateOnly(2026, 6, 15), sent.BookingDate);
        Assert.NotNull(sent.RecurrenceHint);
        Assert.Contains("monthly", sent.RecurrenceHint);
        Assert.Contains("4 occurrences", sent.RecurrenceHint);

        var forBatch = Assert.Single(claude.Examples.ForBatch);
        Assert.Equal("Netflix", forBatch.Counterparty);
        Assert.Equal("Rent", forBatch.CategoryPath);
        Assert.Equal(
            ["Netflix", "Billa"],
            claude.Examples.Recent.Select(example => example.Counterparty).ToList()
        );
    }

    [Fact]
    public async Task Handle_AppliesHighConfidenceAndQueuesLowConfidence()
    {
        // Arrange
        var confident = await ImportAsync("Billa");
        var unsure = await ImportAsync("Something");
        var declined = await ImportAsync("Unknown");
        var claude = ClaudeAnswering([
            new ClaudeCategorySuggestion(confident, Groceries, 0.95m),
            new ClaudeCategorySuggestion(unsure, Rent, 0.5m),
            new ClaudeCategorySuggestion(declined, null, 0.1m),
        ]);

        // Act
        await BackfillAsync(claude.Categorizer);

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
    public async Task Handle_ClaudeFailure_LeavesTransactionsUncategorized()
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

    #endregion

    #region Helpers

    private static readonly NullLogger<CategorizeImportedTransactionsCommandHandler> Logger =
        NullLogger<CategorizeImportedTransactionsCommandHandler>.Instance;

    private async Task BackfillAsync(IClaudeCategorizer categorizer) =>
        await RunBatchesAsync(await PlanBackfillAsync(), categorizer);

    private async Task ImportFollowUpAsync(Guid importBatchId, IClaudeCategorizer categorizer)
    {
        OutgoingMessages batches;
        await using (var session = fixture.Store.LightweightSession())
        {
            batches = await new CategorizeImportedTransactionsCommandHandler().Handle(
                new CategorizeImportedTransactionsCommand(importBatchId),
                session,
                Logger,
                Ct
            );
        }
        await RunBatchesAsync(batches, categorizer);
    }

    /// <summary>Stages 1 and 2, returning the Claude batches the handler cascades.</summary>
    private async Task<OutgoingMessages> PlanBackfillAsync()
    {
        await using var session = fixture.Store.LightweightSession();
        return await new CategorizeImportedTransactionsCommandHandler().Handle(
            new CategorizeUncategorizedTransactionsCommand(),
            session,
            Logger,
            Ct
        );
    }

    /// <summary>Stage 3 the way Wolverine runs it: one session per batch, in order.</summary>
    private async Task RunBatchesAsync(OutgoingMessages batches, IClaudeCategorizer categorizer)
    {
        foreach (var batch in batches.OfType<CategorizeTransactionBatchCommand>())
        {
            await using var session = fixture.Store.LightweightSession();
            await new CategorizeImportedTransactionsCommandHandler().Handle(
                batch,
                session,
                categorizer,
                Logger,
                Ct
            );
        }
    }

    /// <summary>
    /// A Claude substitute that records what it was asked (transactions and
    /// examples of the last call) and answers with the given suggestions.
    /// </summary>
    private sealed class ClaudeCapture
    {
        public List<TransactionToCategorize> SentTransactions { get; } = [];

        public FewShotExamples Examples { get; private set; } = FewShotExamples.None;

        public IEnumerable<Guid> SentIds => SentTransactions.Select(t => t.TransactionId);

        public IClaudeCategorizer Categorizer { get; } = Substitute.For<IClaudeCategorizer>();

        public ClaudeCapture(IReadOnlyList<ClaudeCategorySuggestion> answer)
        {
            Categorizer
                .SuggestAsync(
                    Arg.Do<IReadOnlyList<TransactionToCategorize>>(SentTransactions.AddRange),
                    Arg.Any<IReadOnlyList<CategoryOption>>(),
                    Arg.Do<FewShotExamples>(examples => Examples = examples),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Ok(answer));
        }
    }

    private static ClaudeCapture ClaudeAnswering(IReadOnlyList<ClaudeCategorySuggestion> answer) =>
        new(answer);

    private async Task<TransactionView> LoadAsync(Guid transactionId)
    {
        await using var session = fixture.Store.QuerySession();
        var view = await session.LoadAsync<TransactionView>(transactionId, Ct);
        Assert.NotNull(view);
        return view;
    }

    private async Task<Guid> ImportAsync(
        string counterparty,
        int month = 6,
        int day = 15,
        decimal amount = -12.30m,
        Guid? importBatchId = null
    )
    {
        var transactionId = Guid.CreateVersion7();
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<TransactionView>(
            transactionId,
            new TransactionImported(
                transactionId,
                Guid.NewGuid(),
                new DateOnly(2026, month, day),
                null,
                amount,
                "EUR",
                amount,
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

    private async Task CategorizeAsync(
        Guid transactionId,
        Guid categoryId,
        CategorySource source,
        decimal? confidence = null
    )
    {
        await using var session = fixture.Store.LightweightSession();
        var stream = await session.Events.FetchForWriting<TransactionView>(transactionId, Ct);
        stream.AppendOne(new TransactionCategorized(categoryId, source, confidence));
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
