using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Categorization.History;
using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Categorization.Tests.History;

public sealed class CategorizationHistoryTests
{
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Insurance = Guid.NewGuid();

    private static readonly List<CategoryOption> Categories =
    [
        new(Groceries, "Living › Groceries"),
        new(Insurance, "Health › Health insurance"),
    ];

    #region ConsistentCategoryFor

    [Fact]
    public void ConsistentCategoryFor_TwoManualConfirmationsSameCategory_ReturnsIt()
    {
        // Arrange
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 4),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 5)
        );

        // Act / Assert
        Assert.Equal(Insurance, history.ConsistentCategoryFor("UNIQA"));
    }

    [Fact]
    public void ConsistentCategoryFor_SingleConfirmation_ReturnsNull()
    {
        // Arrange
        var history = Build(Entry("UNIQA", Insurance, CategorySource.Manual, month: 4));

        // Act / Assert
        Assert.Null(history.ConsistentCategoryFor("UNIQA"));
    }

    [Fact]
    public void ConsistentCategoryFor_OnlyManualCounts_IgnoresAiRuleAndHistory()
    {
        // Arrange — the pipeline's own decisions must not reinforce themselves.
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Ai, month: 3),
            Entry("UNIQA", Insurance, CategorySource.Rule, month: 4),
            Entry("UNIQA", Insurance, CategorySource.History, month: 5),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 6)
        );

        // Act / Assert
        Assert.Null(history.ConsistentCategoryFor("UNIQA"));
    }

    [Fact]
    public void ConsistentCategoryFor_ConflictingCategories_ReturnsNull()
    {
        // Arrange
        var history = Build(
            Entry("Amazon", Groceries, CategorySource.Manual, month: 4),
            Entry("Amazon", Insurance, CategorySource.Manual, month: 5),
            Entry("Amazon", Groceries, CategorySource.Manual, month: 6)
        );

        // Act / Assert
        Assert.Null(history.ConsistentCategoryFor("Amazon"));
    }

    [Fact]
    public void ConsistentCategoryFor_MatchesCounterpartyIgnoringCaseAndWhitespace()
    {
        // Arrange
        var history = Build(
            Entry("Uniqa  Versicherung", Insurance, CategorySource.Manual, month: 4),
            Entry("UNIQA VERSICHERUNG", Insurance, CategorySource.Manual, month: 5)
        );

        // Act / Assert
        Assert.Equal(Insurance, history.ConsistentCategoryFor(" uniqa versicherung "));
    }

    [Fact]
    public void ConsistentCategoryFor_DeletedCategory_DoesNotCount()
    {
        // Arrange
        var gone = Guid.NewGuid();
        var history = Build(
            Entry("UNIQA", gone, CategorySource.Manual, month: 4),
            Entry("UNIQA", gone, CategorySource.Manual, month: 5)
        );

        // Act / Assert
        Assert.Null(history.ConsistentCategoryFor("UNIQA"));
    }

    [Fact]
    public void ConsistentCategoryFor_NullOrUnknownCounterparty_ReturnsNull()
    {
        // Arrange
        var history = Build(Entry("UNIQA", Insurance, CategorySource.Manual, month: 4));

        // Act / Assert
        Assert.Null(history.ConsistentCategoryFor(null));
        Assert.Null(history.ConsistentCategoryFor("Nobody"));
    }

    #endregion

    #region ExamplesFor

    [Fact]
    public void ExamplesFor_BatchCounterpartiesNewestFirst_RecentFillSeparately()
    {
        // Arrange — three UNIQA confirmations, only the two newest may be picked for
        // the batch; the recent block is the rest of the history, newest first.
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 1),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 2),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 3),
            Entry("Billa", Groceries, CategorySource.Manual, month: 4),
            Entry("Spar", Groceries, CategorySource.Manual, month: 5),
            Entry("Hofer", Groceries, CategorySource.Manual, month: 6)
        );

        // Act
        var examples = history.ExamplesFor(["UNIQA"], recentCount: 3);

        // Assert
        Assert.Equal(
            ["UNIQA", "UNIQA"],
            examples.ForBatch.Select(example => example.Counterparty).ToList()
        );
        Assert.All(
            examples.ForBatch,
            example => Assert.Equal("Health › Health insurance", example.CategoryPath)
        );
        Assert.Equal(
            ["Hofer", "Spar", "Billa"],
            examples.Recent.Select(example => example.Counterparty).ToList()
        );
    }

    [Fact]
    public void ExamplesFor_RecentBlock_IsTheSameForEveryBatch()
    {
        // Arrange — the recent block is the cached prompt prefix; the batch must not leak into it.
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 1),
            Entry("Billa", Groceries, CategorySource.Manual, month: 2),
            Entry("Spar", Groceries, CategorySource.Manual, month: 3)
        );

        // Act
        var first = history.ExamplesFor(["UNIQA"], recentCount: 2);
        var second = history.ExamplesFor(["Spar", "Billa"], recentCount: 2);

        // Assert
        Assert.Equal(first.Recent, second.Recent);
        Assert.Equal(["Spar", "Billa"], first.Recent.Select(example => example.Counterparty));
        Assert.NotEqual(first.ForBatch, second.ForBatch);
    }

    [Fact]
    public void ExamplesFor_RecentOrder_DoesNotDependOnInputOrderForSameDayEntries()
    {
        // Arrange — two confirmations on one day; whichever way the database returns
        // them, the block must serialize identically or the cache never hits.
        var billa = Entry("Billa", Groceries, CategorySource.Manual, month: 5);
        var spar = Entry("Spar", Groceries, CategorySource.Manual, month: 5);

        // Act
        var oneWay = Build(billa, spar).ExamplesFor([], recentCount: 2).Recent;
        var otherWay = Build(spar, billa).ExamplesFor([], recentCount: 2).Recent;

        // Assert
        Assert.Equal(oneWay, otherWay);
    }

    [Fact]
    public void ExamplesFor_SkipsUnconfirmedAndDuplicateCounterparties()
    {
        // Arrange
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Ai, month: 1),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 2),
            Entry("Nobody", null, null, month: 3)
        );

        // Act
        var examples = history.ExamplesFor(["UNIQA", "uniqa", null], recentCount: 10);

        // Assert
        Assert.Equal("UNIQA", Assert.Single(examples.ForBatch).Counterparty);
        Assert.Equal("UNIQA", Assert.Single(examples.Recent).Counterparty);
    }

    #endregion

    #region RecurrenceHintFor

    [Fact]
    public void RecurrenceHintFor_MonthlySeries_DescribesIntervalAmountAndCount()
    {
        // Arrange — the pending transaction itself is part of the series.
        var history = Build(
            Entry("UNIQA", null, null, month: 3, amount: -142.50m),
            Entry("UNIQA", null, null, month: 4, amount: -142.50m),
            Entry("UNIQA", null, null, month: 5, amount: -142.50m),
            Entry("UNIQA", null, null, month: 6, amount: -142.50m)
        );

        // Act
        var hint = history.RecurrenceHintFor("UNIQA");

        // Assert
        Assert.NotNull(hint);
        Assert.Contains("monthly", hint);
        Assert.Contains("-142.50 EUR", hint);
        Assert.Contains("4 occurrences", hint);
        Assert.Contains("2026-06-15", hint);
    }

    [Fact]
    public void RecurrenceHintFor_UsesEurAmountWhenAvailable()
    {
        // Arrange
        var history = Build(
            Entry("Netflix", null, null, month: 3, amount: -15.99m, amountEur: -14.20m),
            Entry("Netflix", null, null, month: 4, amount: -15.99m, amountEur: -14.30m),
            Entry("Netflix", null, null, month: 5, amount: -15.99m, amountEur: -14.10m)
        );

        // Act
        var hint = history.RecurrenceHintFor("Netflix");

        // Assert
        Assert.NotNull(hint);
        Assert.Contains("-14.20 EUR", hint);
    }

    [Fact]
    public void RecurrenceHintFor_TooFewOrIrregular_ReturnsNull()
    {
        // Arrange
        var history = Build(
            Entry("Billa", null, null, month: 1, amount: -12m),
            Entry("Billa", null, null, month: 2, amount: -80m),
            Entry("Billa", null, null, month: 2, day: 3, amount: -7m),
            Entry("Doctor", null, null, month: 5, amount: -60m),
            Entry("Doctor", null, null, month: 6, amount: -60m)
        );

        // Act / Assert
        Assert.Null(history.RecurrenceHintFor("Billa"));
        Assert.Null(history.RecurrenceHintFor("Doctor"));
        Assert.Null(history.RecurrenceHintFor(null));
    }

    #endregion

    private static CategorizationHistory Build(params HistoryEntry[] entries) =>
        new(entries, Categories);

    private static HistoryEntry Entry(
        string? counterparty,
        Guid? categoryId,
        CategorySource? source,
        int month,
        int day = 15,
        decimal amount = -10m,
        decimal? amountEur = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Counterparty = counterparty,
            Description = $"{counterparty} payment",
            Amount = amount,
            AmountEur = amountEur,
            BookingDate = new DateOnly(2026, month, day),
            CategoryId = categoryId,
            CategorySource = source,
        };
}
