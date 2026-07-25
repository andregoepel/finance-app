using AndreGoepel.FinanceApp.Domain.Imports;

namespace AndreGoepel.FinanceApp.Domain.Tests.Imports;

public sealed class DedupHashTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Compute_SameInput_IsStable()
    {
        // Act
        var first = DedupHash.Compute(AccountId, new DateOnly(2026, 6, 15), -23.45m, "REWE Berlin");
        var second = DedupHash.Compute(
            AccountId,
            new DateOnly(2026, 6, 15),
            -23.45m,
            "REWE Berlin"
        );

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_CosmeticDescriptionDifferences_ProduceSameHash()
    {
        // Act
        var canonical = DedupHash.Compute(
            AccountId,
            new DateOnly(2026, 6, 15),
            -23.45m,
            "REWE Berlin"
        );
        var noisy = DedupHash.Compute(
            AccountId,
            new DateOnly(2026, 6, 15),
            -23.45m,
            "  rewe   BERLIN "
        );

        // Assert
        Assert.Equal(canonical, noisy);
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222", "2026-06-15", "-23.45", "REWE Berlin")] // other account
    [InlineData("11111111-1111-1111-1111-111111111111", "2026-06-16", "-23.45", "REWE Berlin")] // other date
    [InlineData("11111111-1111-1111-1111-111111111111", "2026-06-15", "-23.46", "REWE Berlin")] // other amount
    [InlineData("11111111-1111-1111-1111-111111111111", "2026-06-15", "-23.45", "Lidl Berlin")] // other description
    public void Compute_DifferentBookingFacts_ProduceDifferentHash(
        string accountId,
        string date,
        string amount,
        string description
    )
    {
        // Arrange
        var baseline = DedupHash.Compute(
            AccountId,
            new DateOnly(2026, 6, 15),
            -23.45m,
            "REWE Berlin"
        );

        // Act
        var other = DedupHash.Compute(
            Guid.Parse(accountId),
            DateOnly.Parse(date),
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            description
        );

        // Assert
        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void NormalizeDescription_LowersCasesAndCollapsesWhitespace()
    {
        // Act + Assert
        Assert.Equal("rewe sagt danke", DedupHash.NormalizeDescription("  REWE\t sagt \n DANKE "));
    }
}
