using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Crypto;
using Marten;

namespace AndreGoepel.FinanceApp.Insights;

/// <summary>
/// Implements <see cref="ICryptoService"/> by joining holdings with the cached
/// <see cref="CryptoPrice"/> documents. Unpriced positions appear in the list
/// (so the UI can flag them) but contribute nothing to the total.
/// </summary>
internal sealed class CryptoService(IQuerySession session) : ICryptoService
{
    public async Task<CryptoOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default
    )
    {
        var holdings = await session.Query<CryptoHolding>().ToListAsync(cancellationToken);
        if (holdings.Count == 0)
        {
            return new CryptoOverview(0m, [], null);
        }

        var prices = (
            await session.LoadManyAsync<CryptoPrice>(
                cancellationToken,
                holdings.Select(h => h.CoinGeckoId).Distinct().ToArray()
            )
        ).ToDictionary(p => p.Id);
        var accounts = (
            await session.LoadManyAsync<Account>(
                cancellationToken,
                holdings.Select(h => h.AccountId).Distinct().ToArray()
            )
        ).ToDictionary(a => a.Id);

        var positions = holdings
            .OrderBy(h => h.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(holding =>
            {
                var price = prices.GetValueOrDefault(holding.CoinGeckoId);
                return new CryptoPosition(
                    holding.AccountId,
                    accounts.GetValueOrDefault(holding.AccountId)?.Name ?? "Unknown account",
                    holding.Symbol,
                    holding.CoinGeckoId,
                    holding.Quantity,
                    price?.EurPrice,
                    price is null ? null : holding.Quantity * price.EurPrice
                );
            })
            .ToList();

        var pricedAsOf = holdings
            .Select(h => prices.GetValueOrDefault(h.CoinGeckoId)?.AsOf)
            .Where(asOf => asOf is not null)
            .ToList();
        return new CryptoOverview(
            positions.Sum(p => p.EurValue ?? 0m),
            positions,
            pricedAsOf.Count > 0 ? pricedAsOf.Min() : null
        );
    }
}
