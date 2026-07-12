namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// The latest known EUR price of one crypto asset — one document per CoinGecko
/// id, overwritten on every successful fetch. Unlike <c>ExchangeRate</c> there is
/// no per-date history: valuation only ever needs current prices, and keeping the
/// last snapshot is exactly what the offline fallback requires.
/// </summary>
public sealed class CryptoPrice
{
    /// <summary>Document id — the CoinGecko coin id, e.g. "bitcoin".</summary>
    public required string Id { get; init; }

    public required decimal EurPrice { get; set; }

    /// <summary>When this price was fetched; drives the stale-price hint in the UI.</summary>
    public required DateTimeOffset AsOf { get; set; }
}
