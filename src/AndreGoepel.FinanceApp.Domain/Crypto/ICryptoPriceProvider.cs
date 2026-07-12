namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// Supplies current EUR prices for crypto assets, batched so all holdings are
/// valued with a single API call. Network failures surface as a failed
/// <see cref="Result"/>, never an exception — valuation degrades to cached prices.
/// </summary>
public interface ICryptoPriceProvider
{
    /// <summary>
    /// EUR price per CoinGecko coin id. Ids unknown to the provider are absent
    /// from the result rather than failing the whole batch.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, decimal>>> GetEurPricesAsync(
        IReadOnlyCollection<string> coinGeckoIds,
        CancellationToken cancellationToken = default
    );
}
