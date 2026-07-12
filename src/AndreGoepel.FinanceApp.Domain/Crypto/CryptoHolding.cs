namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// A manually maintained crypto position: how much of one asset a crypto account
/// holds. One document per (account, asset) — the composite id makes saves an
/// upsert. Valuation multiplies the quantity by the cached EUR price
/// (<see cref="CryptoPrice"/>) and anchors the account balance.
/// </summary>
public sealed class CryptoHolding
{
    /// <summary>Document id: <see cref="KeyFor"/> of the account and CoinGecko id.</summary>
    public required string Id { get; init; }

    public required Guid AccountId { get; init; }

    /// <summary>Display ticker, e.g. "BTC". Not used for price lookups.</summary>
    public required string Symbol { get; set; }

    /// <summary>
    /// CoinGecko's coin id (e.g. "bitcoin"), the key the price API expects. Shown
    /// in every coin's coingecko.com URL; an unknown id simply never gets a price.
    /// </summary>
    public required string CoinGeckoId { get; init; }

    public required decimal Quantity { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static string KeyFor(Guid accountId, string coinGeckoId) =>
        $"{accountId}:{coinGeckoId.Trim().ToLowerInvariant()}";
}
