namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// Values crypto holdings in EUR per account: <c>total = Σ quantity × price</c>.
/// An account where any asset has no price is skipped entirely — a partial sum
/// would silently understate the balance, so the previous anchor is kept instead.
/// Pure and side-effect-free.
/// </summary>
public static class CryptoValuationCalculator
{
    public static CryptoValuationResult Compute(
        IReadOnlyList<CryptoHolding> holdings,
        IReadOnlyDictionary<string, CryptoPriceQuote> prices
    )
    {
        var valuations = new List<AccountValuation>();
        var skipped = new List<Guid>();
        foreach (var account in holdings.GroupBy(h => h.AccountId))
        {
            var positions = account
                .Select(h =>
                    prices.TryGetValue(h.CoinGeckoId, out var quote)
                        ? (Value: h.Quantity * quote.EurPrice, quote.AsOf)
                        : ((decimal Value, DateTimeOffset AsOf)?)null
                )
                .ToList();
            if (positions.Any(p => p is null))
            {
                skipped.Add(account.Key);
                continue;
            }

            valuations.Add(
                new AccountValuation(
                    account.Key,
                    positions.Sum(p => p!.Value.Value),
                    // The oldest price used — the honest "as of" for the whole sum.
                    positions.Min(p => p!.Value.AsOf)
                )
            );
        }
        return new CryptoValuationResult(valuations, skipped);
    }
}

/// <summary>One asset's EUR price and when it was fetched.</summary>
public sealed record CryptoPriceQuote(decimal EurPrice, DateTimeOffset AsOf);

/// <summary>The EUR value of one account's holdings as of its oldest price.</summary>
public sealed record AccountValuation(Guid AccountId, decimal TotalEur, DateTimeOffset AsOf);

/// <summary>Valued accounts plus those skipped because a price was missing.</summary>
public sealed record CryptoValuationResult(
    IReadOnlyList<AccountValuation> Valuations,
    IReadOnlyList<Guid> SkippedAccounts
);
