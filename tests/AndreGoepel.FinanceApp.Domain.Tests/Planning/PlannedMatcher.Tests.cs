using AndreGoepel.FinanceApp.Domain.Planning;

namespace AndreGoepel.FinanceApp.Domain.Tests.Planning;

public class PlannedMatcherTests
{
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly DateOnly Due = new(2026, 3, 15);

    private static PlannedMatchCriteria Criteria(
        decimal amount = -50m,
        decimal tolerance = 0.1m,
        int window = 5,
        string? pattern = null,
        Guid? account = null
    ) => new(amount, Due, tolerance, window, pattern, account);

    private static MatchCandidate Txn(
        DateOnly date,
        decimal amount,
        string? counterparty = null,
        Guid? account = null
    ) => new(Guid.NewGuid(), account ?? AccountA, date, amount, counterparty, "desc");

    [Fact]
    public void FindMatch_WithinToleranceAndWindow_Matches()
    {
        var candidate = Txn(new DateOnly(2026, 3, 16), -48m); // -48 within ±10% of -50, 1 day off
        var match = PlannedMatcher.FindMatch(Criteria(), [candidate]);
        Assert.Equal(candidate.Id, match);
    }

    [Fact]
    public void FindMatch_OutsideAmountTolerance_NoMatch()
    {
        var candidate = Txn(new DateOnly(2026, 3, 15), -70m); // 40% off
        Assert.Null(PlannedMatcher.FindMatch(Criteria(), [candidate]));
    }

    [Fact]
    public void FindMatch_OutsideDateWindow_NoMatch()
    {
        var candidate = Txn(new DateOnly(2026, 3, 25), -50m); // 10 days off, window 5
        Assert.Null(PlannedMatcher.FindMatch(Criteria(), [candidate]));
    }

    [Fact]
    public void FindMatch_WrongSign_NoMatch()
    {
        // Planned expense (−50); an income of +50 must not match.
        var candidate = Txn(new DateOnly(2026, 3, 15), 50m);
        Assert.Null(PlannedMatcher.FindMatch(Criteria(), [candidate]));
    }

    [Fact]
    public void FindMatch_CounterpartyPattern_Filters()
    {
        var wrong = Txn(new DateOnly(2026, 3, 15), -50m, "Random");
        var right = Txn(new DateOnly(2026, 3, 15), -50m, "Landlord GmbH");
        var match = PlannedMatcher.FindMatch(Criteria(pattern: "landlord"), [wrong, right]);
        Assert.Equal(right.Id, match);
    }

    [Fact]
    public void FindMatch_ExpectedAccount_Filters()
    {
        var otherAccount = Guid.NewGuid();
        var wrong = Txn(new DateOnly(2026, 3, 15), -50m, account: otherAccount);
        var right = Txn(new DateOnly(2026, 3, 15), -50m, account: AccountA);
        var match = PlannedMatcher.FindMatch(Criteria(account: AccountA), [wrong, right]);
        Assert.Equal(right.Id, match);
    }

    [Fact]
    public void FindMatch_PrefersClosestDate()
    {
        var far = Txn(new DateOnly(2026, 3, 18), -50m);
        var near = Txn(new DateOnly(2026, 3, 15), -50m);
        var match = PlannedMatcher.FindMatch(Criteria(), [far, near]);
        Assert.Equal(near.Id, match);
    }
}
