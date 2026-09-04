using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Domain.Tests.Transactions;

public sealed class TransferMatcherTests
{
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();
    private static readonly Guid AccountC = Guid.NewGuid();
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
    public void FindPairs_OneDayApart_NoMatch()
    {
        // Arrange — the day must be identical now, no leeway at all.
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day.AddDays(1), 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
    }

    [Fact]
    public void FindPairs_SmallAmountDifference_NoMatch()
    {
        // Arrange — a one-cent difference used to slip through the old fuzzy tolerance.
        var outgoing = Txn(AccountA, Day, -100m);
        var incoming = Txn(AccountB, Day, 99.99m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Empty(result.Fuzzy);
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
        // Arrange — two identical €100 outgoing legs on different accounts, one
        // incoming leg: neither pairing is certain, so both must be reviewed
        // rather than guessed.
        var outgoing1 = Txn(AccountA, Day, -100m);
        var outgoing2 = Txn(AccountC, Day, -100m);
        var incoming = Txn(AccountB, Day, 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing1, outgoing2, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Equal(2, result.Fuzzy.Count);
        Assert.All(result.Fuzzy, p => Assert.Equal(incoming.Id, p.IncomingId));
    }

    [Fact]
    public void FindPairs_UnrelatedExactPairsElsewhereInThePool_AllAutoLink()
    {
        // Arrange — regression guard: many independent same-day, same-amount
        // transfers must not make each other look "ambiguous".
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
    public void FindPairs_DifferentCurrencies_NeverExactEvenWhenEurAmountsMatchExactly()
    {
        // Arrange — same day, EUR amounts cancel out exactly, but the FX leg
        // always needs a human glance rather than auto-linking.
        var outgoing = Txn(AccountA, Day, -100m, "USD");
        var incoming = Txn(AccountB, Day, 100m, "EUR");

        // Act
        var result = TransferMatcher.FindPairs([outgoing, incoming]);

        // Assert
        Assert.Empty(result.Exact);
        var pair = Assert.Single(result.Fuzzy);
        Assert.False(pair.SameCurrency);
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
        // Arrange — one outgoing leg with two exactly-matching incoming
        // candidates on different accounts: both should surface for review
        // rather than the matcher silently picking one.
        var outgoing = Txn(AccountA, Day, -100m);
        var candidate1 = Txn(AccountB, Day, 100m);
        var candidate2 = Txn(AccountC, Day, 100m);

        // Act
        var result = TransferMatcher.FindPairs([outgoing, candidate1, candidate2]);

        // Assert
        Assert.Empty(result.Exact);
        Assert.Equal(2, result.Fuzzy.Count);
        Assert.All(result.Fuzzy, p => Assert.Equal(outgoing.Id, p.OutgoingId));
    }
}
