namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Read model for the crypto dashboard tile and holdings page: every holding
/// joined with its last cached EUR price. Reads only cached prices — never
/// triggers an API call; refreshing is <c>RefreshCryptoValuationsCommand</c>'s job.
/// </summary>
public interface ICryptoService
{
    Task<CryptoOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Total EUR value of all priced positions, the per-asset breakdown, and the age
/// of the oldest price used (for the stale-price hint).
/// </summary>
public sealed record CryptoOverview(
    decimal TotalEur,
    IReadOnlyList<CryptoPosition> Positions,
    DateTimeOffset? OldestPriceAt
);

/// <summary>One holding with its cached price; nulls when no price is known yet.</summary>
public sealed record CryptoPosition(
    Guid AccountId,
    string AccountName,
    string Symbol,
    string CoinGeckoId,
    decimal Quantity,
    decimal? EurPrice,
    decimal? EurValue
);
