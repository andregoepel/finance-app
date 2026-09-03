using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Domain.Tests.Transactions;

public sealed class TransferMatcherTests
{
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 3, 15);

    private static TransferCandidate Txn(
        Guid account,
        DateOnly date,
        decimal amountEur,
        string currency = "EUR"
    ) => new(Guid.NewGuid(), account, date, currency, amountEur);

    [Fact]
    public void FindPairs_SameDaySameAmountOppositeAccounts_IsExact()
    {
        // Arrange
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day, 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        var pair = Assert.Single(result.Exact);
        Assert.Equal(outgoing.Id, pair.OutgoingId);
        Assert.Equal(incoming.Id, pair.IncomingId);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_OneDayApart_IsStillExact()
    {
        // Arrange
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day.AddDays(1), 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Single(result.Exact);
    }

    [Fact]
    public void FindPairs_SameAccount_NeverPairs()
    {
        // Arrange — both legs on the same account can't be a transfer.
        var a = Txn(AccountA, Day, -100m);
        var b = Txn(AccountA, Day, 100m);

        // Act
        var result = TransferMatcher.FindPairs([a, b]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_SameSign_NeverPairs()
    {
        // Arrange — two outgoing legs are not a transfer pair.
        var a = Txn(AccountA, Day, -100m);
        var b = Txn(AccountB, Day, -100m);

        // Act
        var result = TransferMatcher.FindPairs([a, b]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_AmbiguousExactCandidates_DemotedToFuzzy()
    {
        // Arrange — two identical €100 outgoing legs, one incoming leg: neither
        // pairing is certain, so both must be reviewed rather than guessed.
        var outgoing1 = Txn(AccountA, Day, -100m);
        var outgoing2 = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day, 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing1, outgoing2, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.NotEmpty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_UnrelatedExactPairsElsewhereInThePool_AllAutoLink()
    {
        // Arrange — regression guard: many independent same-day, same-amount
        // transfers must not make each other look "ambiguous" just because they
        // share the identical (0 days, €0) score.
        var pair1Out = Txn(AccountA, Day, -100m);
        var pair1In = Txn(AccountB, Day, 100m);
        var pair2Out = Txn(AccountA, Day, -250m);
        var pair2In = Txn(AccountB, Day, 250m);

        // Act
        var result = TransferMatcher.FindPairs([pair1Out, pair1In, pair2Out, pair2In]);

        // Assert
        Assert.Equal(2, result.Exact.Count);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_WithinFuzzyToleranceButNotExact_IsFuzzy()
    {
        // Arrange — 3 days apart, 1.50 EUR off; within ±5 days / max(1%, €2).
        var outgoing = Txn(AccountA, Day, -500m);
        var incoming = Txn(AccountB, Day.AddDays(3), 498.5m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        var pair = Assert.Single(result.Fuzzy);
        Assert.Equal(3, pair.DayDifference);
        Assert.Equal(1.5m, pair.AmountDifferenceEur);
    }

    [Fact]
    public void FindPairs_OutsideDateWindow_NoMatch()
    {
        // Arrange — 6 days apart, exceeds the 5-day fuzzy window.
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day.AddDays(6), 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_OutsideAmountTolerance_NoMatch()
    {
        // Arrange — same day, but the difference exceeds both the 1% and the €2 floor.
        var outgoing = Txn(AccountA, Day, -50m);
        var incoming = Txn(AccountB, Day, 45m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_DifferentCurrencies_NeverExactEvenWhenEurAmountsMatch()
    {
        // Arrange — an FX leg must always go through review, never auto-link.
        var outgoing = Txn(AccountA, Day, -100m, "USD");
        var incoming = Txn(AccountB, Day, 100m, "EUR");

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Single(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_TransactionWithoutEurAmount_IsIgnored()
    {
        // Arrange
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = new TransferCandidate(Guid.NewGuid(), AccountB, Day, "EUR", null);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_Exact_NeverReusesATransaction()
    {
        // Arrange — two independent exact pairs; the exact tier must not double-use a leg.
        var pair1Out = Txn(AccountA, Day, -100m);
        var pair1In = Txn(AccountB, Day, 100m);
        var pair2Out = Txn(AccountA, Day, -250m);
        var pair2In = Txn(AccountB, Day, 250m);

        // Act
        var result = TransferMatcher.FindPairs([pair1Out, pair1In, pair2Out, pair2In]);

        // Assert
        var usedIds = result.Exact.SelectMany(p => new[] { p.OutgoingId, p.IncomingId }).ToList();
        Assert.Equal(usedIds.Count, usedIds.Distinct().Count());
    }

    [Fact]
    public void FindPairs_Fuzzy_MayOfferTheSameLegForMultipleCandidates()
    {
        // Arrange — one outgoing leg with two plausible (but not exact) incoming
        // candidates on a different account: both should surface for review
        // rather than the matcher silently picking one.
        var outgoing = Txn(AccountA, Day, -100m);
        var closerCandidate = Txn(AccountB, Day, 99m);
        var fartherCandidate = Txn(AccountB, Day.AddDays(1), 101m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, closerCandidate, fartherCandidate]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Equal(2, result.Fuzzy.Count);
        Assert.All(result.Fuzzy, p => Assert.Equal(outgoing.Id, p.OutgoingId));
    }
}
