using AndreGoepel.FinanceApp.Domain.Crypto;

namespace AndreGoepel.FinanceApp.Domain.Tests.Crypto;

public sealed class CryptoValuationCalculatorTests
{
    private static readonly DateTimeOffset Newer = new(2026, 7, 12, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Older = new(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_MultipleAssetsOnOneAccount_SumsQuantityTimesPrice()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var holdings = new[]
        {
            Holding(accountId, "bitcoin", 0.5m),
            Holding(accountId, "ethereum", 10m),
        };
        var prices = new Dictionary<string, CryptoPriceQuote>
        {
            ["bitcoin"] = new(90_000m, Newer),
            ["ethereum"] = new(3_000m, Newer),
        };

        // Act
        var result = CryptoValuationCalculator.Compute(holdings, prices);

        // Assert
        var valuation = Assert.Single(result.Valuations);
        Assert.Equal(accountId, valuation.AccountId);
        Assert.Equal(75_000m, valuation.TotalEur);
        Assert.Empty(result.SkippedAccounts);
    }

    [Fact]
    public void Compute_AccountWithAnUnpricedAsset_IsSkippedEntirely()
    {
        // Arrange — a partial sum would understate the balance, so none is produced.
        var pricedAccount = Guid.NewGuid();
        var unpricedAccount = Guid.NewGuid();
        var holdings = new[]
        {
            Holding(pricedAccount, "bitcoin", 1m),
            Holding(unpricedAccount, "bitcoin", 1m),
            Holding(unpricedAccount, "no-such-coin", 5m),
        };
        var prices = new Dictionary<string, CryptoPriceQuote> { ["bitcoin"] = new(90_000m, Newer) };

        // Act
        var result = CryptoValuationCalculator.Compute(holdings, prices);

        // Assert
        Assert.Equal(pricedAccount, Assert.Single(result.Valuations).AccountId);
        Assert.Equal(unpricedAccount, Assert.Single(result.SkippedAccounts));
    }

    [Fact]
    public void Compute_MixedPriceAges_UsesOldestAsOf()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var holdings = new[]
        {
            Holding(accountId, "bitcoin", 1m),
            Holding(accountId, "ethereum", 1m),
        };
        var prices = new Dictionary<string, CryptoPriceQuote>
        {
            ["bitcoin"] = new(90_000m, Newer),
            ["ethereum"] = new(3_000m, Older),
        };

        // Act
        var result = CryptoValuationCalculator.Compute(holdings, prices);

        // Assert
        Assert.Equal(Older, Assert.Single(result.Valuations).AsOf);
    }

    [Fact]
    public void Compute_NoHoldings_ReturnsEmpty()
    {
        // Act
        var result = CryptoValuationCalculator.Compute(
            [],
            new Dictionary<string, CryptoPriceQuote>()
        );

        // Assert
        Assert.Empty(result.Valuations);
        Assert.Empty(result.SkippedAccounts);
    }

    private static CryptoHolding Holding(Guid accountId, string coinGeckoId, decimal quantity) =>
        new()
        {
            Id = CryptoHolding.KeyFor(accountId, coinGeckoId),
            AccountId = accountId,
            Symbol = coinGeckoId.ToUpperInvariant(),
            CoinGeckoId = coinGeckoId,
            Quantity = quantity,
        };
}
