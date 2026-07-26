using System.Text.Json;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Exchange;

namespace AndreGoepel.FinanceApp.Connectors.Exchange;

/// <summary>
/// ECB reference rates via the free Frankfurter API (no key). A request for a
/// weekend/holiday returns the nearest prior business day; the response's own
/// <c>date</c> field is the actual rate date. <c>public</c> so Wolverine can
/// inline-construct it as a handler dependency (like the Claude client).
/// </summary>
public sealed class FrankfurterExchangeRateProvider(IHttpClientFactory httpClientFactory)
    : IExchangeRateProvider
{
    internal const string HttpClientName = "frankfurter";

    public async Task<Result<EurRate>> GetEurRateAsync(
        string currency,
        DateOnly date,
        CancellationToken cancellationToken = default
    )
    {
        if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Ok(new EurRate(1m, date));
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await httpClient.GetAsync(
                $"{date:yyyy-MM-dd}?from={Uri.EscapeDataString(currency)}&to=EUR",
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<EurRate>(
                    $"Frankfurter returned {(int)response.StatusCode} for {currency} on {date:yyyy-MM-dd}."
                );
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (
                !root.TryGetProperty("rates", out var rates)
                || !rates.TryGetProperty("EUR", out var eur)
            )
            {
                return Result.Fail<EurRate>(
                    $"Frankfurter has no EUR rate for {currency} (unsupported currency?)."
                );
            }

            var rateDate =
                root.TryGetProperty("date", out var dateElement)
                && DateOnly.TryParse(dateElement.GetString(), out var parsed)
                    ? parsed
                    : date;
            return Result.Ok(new EurRate(eur.GetDecimal(), rateDate));
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<EurRate>($"Frankfurter unreachable: {exception.Message}");
        }
        catch (JsonException exception)
        {
            return Result.Fail<EurRate>($"Unreadable Frankfurter response: {exception.Message}");
        }
    }
}
