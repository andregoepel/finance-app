using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using Marten;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// Revalues every crypto account from its holdings: one batched price fetch, then
/// the EUR sum is written as the account's balance anchor so net worth picks it
/// up unchanged. A price-API outage degrades to the last cached prices and never
/// fails the command — published after each scheduled sync and from the UI.
/// </summary>
public sealed record RefreshCryptoValuationsCommand;

/// <summary>What a refresh run did, for UI feedback.</summary>
public sealed record CryptoValuationSummary(
    int AccountsUpdated,
    int AccountsSkipped,
    bool UsedCachedPrices
);

public static class RefreshCryptoValuationsCommandHandler
{
    public static async Task<Result<CryptoValuationSummary>> Handle(
        RefreshCryptoValuationsCommand command,
        IDocumentSession session,
        ICryptoPriceProvider priceProvider,
        TimeProvider timeProvider,
        ILogger<RefreshCryptoValuationsCommand> logger,
        CancellationToken cancellationToken
    )
    {
        var holdings = await session.Query<CryptoHolding>().ToListAsync(cancellationToken);
        if (holdings.Count == 0)
        {
            return Result.Ok(new CryptoValuationSummary(0, 0, false));
        }

        var coinIds = holdings.Select(h => h.CoinGeckoId).Distinct().ToList();
        var quotes = await ResolveQuotesAsync(
            session,
            priceProvider,
            timeProvider,
            coinIds,
            logger,
            cancellationToken
        );
        await session.SaveChangesAsync(cancellationToken);

        var result = CryptoValuationCalculator.Compute(holdings, quotes.Prices);
        var updated = 0;
        var skipped = result.SkippedAccounts.Count;
        foreach (var valuation in result.Valuations)
        {
            var stored = await SetAccountBalanceCommandHandler.Handle(
                new SetAccountBalanceCommand(
                    valuation.AccountId,
                    // A multi-asset account has no single native currency; the EUR
                    // sum doubles as the native balance.
                    valuation.TotalEur,
                    valuation.TotalEur,
                    valuation.AsOf
                ),
                session,
                cancellationToken
            );
            if (stored.IsFailure)
            {
                logger.LogWarning(
                    "Crypto valuation not stored for account {AccountId}: {Reason}",
                    valuation.AccountId,
                    stored.Error
                );
                skipped++;
                continue;
            }
            updated++;
        }

        return Result.Ok(new CryptoValuationSummary(updated, skipped, quotes.UsedCache));
    }

    // Fresh prices when the API is reachable (upserting the cache), otherwise the cached snapshots; unpriced ids also fall back to the cache.
    private static async Task<(
        IReadOnlyDictionary<string, CryptoPriceQuote> Prices,
        bool UsedCache
    )> ResolveQuotesAsync(
        IDocumentSession session,
        ICryptoPriceProvider priceProvider,
        TimeProvider timeProvider,
        IReadOnlyList<string> coinIds,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var quotes = new Dictionary<string, CryptoPriceQuote>();
        var fetched = await priceProvider.GetEurPricesAsync(coinIds, cancellationToken);
        if (fetched.IsSuccess)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var (coinId, price) in fetched.Value!)
            {
                var cached = await session.LoadAsync<CryptoPrice>(coinId, cancellationToken);
                if (cached is null)
                {
                    cached = new CryptoPrice
                    {
                        Id = coinId,
                        EurPrice = price,
                        AsOf = now,
                    };
                }
                else
                {
                    cached.EurPrice = price;
                    cached.AsOf = now;
                }
                session.Store(cached);
                quotes[coinId] = new CryptoPriceQuote(price, now);
            }
        }
        else
        {
            logger.LogWarning(
                "Crypto prices unavailable, falling back to cached prices: {Reason}",
                fetched.Error
            );
        }

        foreach (var coinId in coinIds.Where(id => !quotes.ContainsKey(id)))
        {
            var cached = await session.LoadAsync<CryptoPrice>(coinId, cancellationToken);
            if (cached is not null)
            {
                quotes[coinId] = new CryptoPriceQuote(cached.EurPrice, cached.AsOf);
            }
        }
        return (quotes, fetched.IsFailure);
    }
}
