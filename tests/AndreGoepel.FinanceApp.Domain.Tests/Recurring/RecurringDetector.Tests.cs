using AndreGoepel.FinanceApp.Domain.Recurring;

namespace AndreGoepel.FinanceApp.Domain.Tests.Recurring;

public sealed class RecurringDetectorTests
{
    private static RecurringCandidate Sub(string name, int year, int month, decimal amount) =>
        new(name, new DateOnly(year, month, 15), amount);

    [Fact]
    public void Detect_MonthlySubscription_IsFound()
    {
        // Arrange — Netflix on the 15th of four consecutive months, same amount.
        var candidates = new List<RecurringCandidate>
        {
            Sub("Netflix", 2026, 1, -12.99m),
            Sub("Netflix", 2026, 2, -12.99m),
            Sub("Netflix", 2026, 3, -12.99m),
            Sub("Netflix", 2026, 4, -12.99m),
        };

        // Act
        var series = RecurringDetector.Detect(candidates);

        // Assert
        var netflix = Assert.Single(series);
        Assert.Equal("Netflix", netflix.Counterparty);
        Assert.Equal(RecurrenceInterval.Monthly, netflix.Interval);
        Assert.Equal(-12.99m, netflix.TypicalAmount);
        Assert.Equal(4, netflix.Occurrences);
        Assert.Equal(new DateOnly(2026, 4, 15), netflix.LastSeen);
        Assert.Equal(new DateOnly(2026, 5, 15), netflix.NextExpected);
    }

    [Fact]
    public void Detect_GroupsCounterpartyCaseAndSpacingInsensitively()
    {
        // Arrange
        var candidates = new List<RecurringCandidate>
        {
            new("Spotify AB", new DateOnly(2026, 1, 3), -9.99m),
            new("spotify  ab", new DateOnly(2026, 2, 3), -9.99m),
            new("SPOTIFY AB", new DateOnly(2026, 3, 3), -9.99m),
        };

        // Act
        var series = RecurringDetector.Detect(candidates);

        // Assert
        Assert.Equal(RecurrenceInterval.Monthly, Assert.Single(series).Interval);
    }

    [Fact]
    public void Detect_IrregularOrTooFew_AreIgnored()
    {
        // Arrange — one-offs and an irregular pair.
        var candidates = new List<RecurringCandidate>
        {
            new("Random Shop", new DateOnly(2026, 1, 2), -40m),
            new("Random Shop", new DateOnly(2026, 1, 20), -5m),
            new("One Off", new DateOnly(2026, 3, 9), -100m),
        };

        // Act
        var series = RecurringDetector.Detect(candidates);

        // Assert
        Assert.Empty(series);
    }

    [Fact]
    public void Detect_VaryingAmountsBeyondTolerance_AreRejected()
    {
        // Arrange — monthly cadence but wildly different amounts.
        var candidates = new List<RecurringCandidate>
        {
            Sub("Shop", 2026, 1, -10m),
            Sub("Shop", 2026, 2, -80m),
            Sub("Shop", 2026, 3, -12m),
        };

        // Act
        var series = RecurringDetector.Detect(candidates);

        // Assert
        Assert.Empty(series);
    }
}
