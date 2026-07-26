using System.Text.Json;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Crypto;

namespace AndreGoepel.FinanceApp.Connectors.Crypto;

/// <summary>
/// Current EUR prices via the free CoinGecko API (no key). One batched
/// <c>simple/price</c> call prices every requested coin id; ids CoinGecko does
/// not know are simply absent from the response (and the result). <c>public</c>
/// so Wolverine can inline-construct it as a handler dependency.
/// </summary>
public sealed class CoinGeckoPriceProvider(IHttpClientFactory httpClientFactory)
    : ICryptoPriceProvider
{
    internal const string HttpClientName = "coingecko";

    public async Task<Result<IReadOnlyDictionary<string, decimal>>> GetEurPricesAsync(
        IReadOnlyCollection<string> coinGeckoIds,
        CancellationToken cancellationToken = default
    )
    {
        if (coinGeckoIds.Count == 0)
        {
            return Result.Ok<IReadOnlyDictionary<string, decimal>>(
                new Dictionary<string, decimal>()
            );
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await httpClient.GetAsync(
                $"simple/price?ids={Uri.EscapeDataString(string.Join(',', coinGeckoIds))}&vs_currencies=eur",
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<IReadOnlyDictionary<string, decimal>>(
                    $"CoinGecko returned {(int)response.StatusCode}."
                );
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            var prices = new Dictionary<string, decimal>();
            foreach (var coin in document.RootElement.EnumerateObject())
            {
                if (coin.Value.TryGetProperty("eur", out var eur))
                {
                    prices[coin.Name] = eur.GetDecimal();
                }
            }
            return Result.Ok<IReadOnlyDictionary<string, decimal>>(prices);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<IReadOnlyDictionary<string, decimal>>(
                $"CoinGecko unreachable: {exception.Message}"
            );
        }
        catch (JsonException exception)
        {
            return Result.Fail<IReadOnlyDictionary<string, decimal>>(
                $"Unreadable CoinGecko response: {exception.Message}"
            );
        }
    }
}
