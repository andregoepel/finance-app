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
    public void ExamplesFor_SameCounterpartyFirst_ThenMostRecentFill()
    {
        // Arrange — three UNIQA confirmations, only two may be picked for it;
        // the fill comes from the rest, newest first.
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 1),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 2),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 3),
            Entry("Billa", Groceries, CategorySource.Manual, month: 4),
            Entry("Spar", Groceries, CategorySource.Manual, month: 5),
            Entry("Hofer", Groceries, CategorySource.Manual, month: 6)
        );

        // Act
        var examples = history.ExamplesFor(["UNIQA"], maxTotal: 4);

        // Assert
        Assert.Equal(
            ["UNIQA", "UNIQA", "Hofer", "Spar"],
            examples.Select(example => example.Counterparty).ToList()
        );
        Assert.All(
            examples.Take(2),
            example => Assert.Equal("Health › Health insurance", example.CategoryPath)
        );
    }

    [Fact]
    public void ExamplesFor_CounterpartyExamplesAreNeverCutByMaxTotal()
    {
        // Arrange
        var history = Build(
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 1),
            Entry("UNIQA", Insurance, CategorySource.Manual, month: 2),
            Entry("Billa", Groceries, CategorySource.Manual, month: 3),
            Entry("Billa", Groceries, CategorySource.Manual, month: 4),
            Entry("Spar", Groceries, CategorySource.Manual, month: 5)
        );

        // Act
        var examples = history.ExamplesFor(["UNIQA", "Billa"], maxTotal: 1);

        // Assert
        Assert.Equal(4, examples.Count);
        Assert.DoesNotContain(examples, example => example.Counterparty == "Spar");
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
        var examples = history.ExamplesFor(["UNIQA", "uniqa", null], maxTotal: 10);

        // Assert
        var example = Assert.Single(examples);
        Assert.Equal("UNIQA", example.Counterparty);
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
