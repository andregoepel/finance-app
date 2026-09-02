using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// HTTP implementation of <see cref="IWiseApiClient"/>. The token and environment
/// are passed per call so no secret or base URL is held in a field.
/// </summary>
internal sealed partial class WiseApiClient(IHttpClientFactory httpClientFactory) : IWiseApiClient
{
    internal const string HttpClientName = "wise";

    /// <summary>Safety cap on cursor pagination — 100 activities per page.</summary>
    private const int MaxActivityPages = 20;

    private static string BaseUrl(ProviderEnvironment environment) =>
        environment == ProviderEnvironment.Sandbox
            ? "https://api.wise-sandbox.com"
            : "https://api.wise.com";

    public async Task<Result<IReadOnlyList<WiseProfile>>> GetProfilesAsync(
        string apiToken,
        ProviderEnvironment environment,
        CancellationToken cancellationToken = default
    )
    {
        var json = await GetAsync(apiToken, environment, "/v1/profiles", cancellationToken);
        if (json.IsFailure)
        {
            return Result.Fail<IReadOnlyList<WiseProfile>>(json.Error!);
        }

        try
        {
            using var document = JsonDocument.Parse(json.Value!);
            var profiles = document
                .RootElement.EnumerateArray()
                .Select(p => new WiseProfile(
                    p.GetProperty("id").GetInt64(),
                    p.TryGetProperty("type", out var type) ? type.GetString() ?? "" : ""
                ))
                .ToList();
            return Result.Ok<IReadOnlyList<WiseProfile>>(profiles);
        }
        catch (JsonException exception)
        {
            return Result.Fail<IReadOnlyList<WiseProfile>>(
                $"Unreadable Wise profiles response: {exception.Message}"
            );
        }
    }

    public async Task<Result<IReadOnlyList<WiseBalance>>> GetBalancesAsync(
        string apiToken,
        ProviderEnvironment environment,
        long profileId,
        CancellationToken cancellationToken = default
    )
    {
        var json = await GetAsync(
            apiToken,
            environment,
            $"/v4/profiles/{profileId}/balances?types=STANDARD,SAVINGS",
            cancellationToken
        );
        if (json.IsFailure)
        {
            return Result.Fail<IReadOnlyList<WiseBalance>>(json.Error!);
        }

        try
        {
            using var document = JsonDocument.Parse(json.Value!);
            var balances = document
                .RootElement.EnumerateArray()
                .Select(b => new WiseBalance(
                    b.GetProperty("id").GetInt64(),
                    b.GetProperty("currency").GetString() ?? "",
                    b.GetProperty("amount").GetProperty("value").GetDecimal(),
                    // Deliberately not defaulted to "STANDARD": a missing/unparseable type
                    // must not silently masquerade as a normal balance — WiseConnector fails
                    // loudly on anything it doesn't recognize as STANDARD or SAVINGS, rather
                    // than risk syncing a jar's full transaction history onto the household.
                    b.TryGetProperty("type", out var type)
                        ? type.GetString() ?? ""
                        : "",
                    b.TryGetProperty("name", out var name) ? name.GetString() : null,
                    // Same fail-closed reasoning as Type: a missing "primary" must not
                    // default to true (syncable) — that would risk duplicating the real
                    // primary balance's transactions onto this one, same as the Type bug.
                    b.TryGetProperty("primary", out var primary)
                        && primary.ValueKind == JsonValueKind.True
                ))
                .ToList();
            return Result.Ok<IReadOnlyList<WiseBalance>>(balances);
        }
        catch (JsonException exception)
        {
            return Result.Fail<IReadOnlyList<WiseBalance>>(
                $"Unreadable Wise balances response: {exception.Message}"
            );
        }
    }

    public async Task<Result<IReadOnlyList<WiseActivity>>> GetActivitiesAsync(
        string apiToken,
        ProviderEnvironment environment,
        long profileId,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default
    )
    {
        var basePath =
            $"/v1/profiles/{profileId}/activities"
            + $"?since={since:yyyy-MM-dd}T00:00:00.000Z"
            + $"&until={until:yyyy-MM-dd}T23:59:59.999Z"
            + "&size=100";

        var activities = new List<WiseActivity>();
        string? cursor = null;
        for (var page = 0; page < MaxActivityPages; page++)
        {
            var path = cursor is null
                ? basePath
                : $"{basePath}&nextCursor={Uri.EscapeDataString(cursor)}";
            var json = await GetAsync(apiToken, environment, path, cancellationToken);
            if (json.IsFailure)
            {
                return Result.Fail<IReadOnlyList<WiseActivity>>(json.Error!);
            }

            try
            {
                cursor = ParseActivitiesPage(json.Value!, activities);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
            {
                return Result.Fail<IReadOnlyList<WiseActivity>>(
                    $"Unreadable Wise activities response: {exception.Message}"
                );
            }

            if (cursor is null)
            {
                break;
            }
        }

        return Result.Ok<IReadOnlyList<WiseActivity>>(activities);
    }

    /// <summary>
    /// Parses one activities page into <paramref name="target"/> and returns the
    /// next-page cursor (null when exhausted). Non-monetary entries (no display
    /// amount) are skipped — they are profile events, not transactions.
    /// Internal for fixture tests.
    /// </summary>
    internal static string? ParseActivitiesPage(string json, List<WiseActivity> target)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.GetProperty("activities").EnumerateArray())
        {
            var primaryAmount = item.TryGetProperty("primaryAmount", out var amountElement)
                ? amountElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(primaryAmount))
            {
                continue;
            }
            if (!TryParseDisplayAmount(primaryAmount, out var amount, out var currency))
            {
                continue;
            }

            // Conversions carry the source side ("11,002 EUR" spent) in the
            // secondary amount; keep it so the caller can book both balances.
            decimal? secondaryAmount = null;
            string? secondaryCurrency = null;
            if (
                item.TryGetProperty("secondaryAmount", out var secondaryElement)
                && !string.IsNullOrWhiteSpace(secondaryElement.GetString())
                && TryParseDisplayAmount(
                    secondaryElement.GetString()!,
                    out var parsedSecondary,
                    out var parsedSecondaryCurrency
                )
            )
            {
                secondaryAmount = Math.Abs(parsedSecondary);
                secondaryCurrency = parsedSecondaryCurrency;
            }

            target.Add(
                new WiseActivity(
                    Id: item.GetProperty("id").GetString() ?? "",
                    Type: item.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                    Status: item.TryGetProperty("status", out var status)
                        ? status.GetString() ?? ""
                        : "",
                    Date: DateOnly.FromDateTime(
                        item.GetProperty("createdOn").GetDateTimeOffset().UtcDateTime
                    ),
                    Amount: amount,
                    Currency: currency,
                    Title: StripMarkup(
                        item.TryGetProperty("title", out var title) ? title.GetString() : null
                    ),
                    Description: StripMarkup(
                        item.TryGetProperty("description", out var description)
                            ? description.GetString()
                            : null
                    ),
                    RawJson: item.GetRawText(),
                    SecondaryAmount: secondaryAmount,
                    SecondaryCurrency: secondaryCurrency
                )
            );
        }

        return
            document.RootElement.TryGetProperty("cursor", out var cursor)
            && cursor.ValueKind == JsonValueKind.String
            ? cursor.GetString()
            : null;
    }

    /// <summary>
    /// Parses Wise's display amount ("666 EUR", "&lt;positive&gt;+ 3.53 GBP&lt;/positive&gt;",
    /// "- 1,000.66 USD") into a signed decimal + currency. Credits carry Wise's
    /// positive markup or an explicit plus; everything else is money out.
    /// </summary>
    internal static bool TryParseDisplayAmount(
        string display,
        out decimal amount,
        out string currency
    )
    {
        amount = 0;
        currency = "";

        var isCredit =
            display.Contains("<positive>", StringComparison.OrdinalIgnoreCase)
            || StripMarkup(display)!.TrimStart().StartsWith('+');
        var text = StripMarkup(display)!.Trim();
        var isNegative = text.StartsWith('-') || text.StartsWith('−');
        text = text.TrimStart('+', '-', '−', ' ');

        var lastSpace = text.LastIndexOf(' ');
        if (lastSpace <= 0)
        {
            return false;
        }
        currency = text[(lastSpace + 1)..].Trim();
        var number = text[..lastSpace].Replace(",", "").Trim();
        if (
            currency.Length != 3
            || !decimal.TryParse(
                number,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount
            )
        )
        {
            return false;
        }

        if (isNegative || !isCredit)
        {
            amount = -Math.Abs(amount);
        }
        return true;
    }

    /// <summary>Removes Wise's presentation markup (&lt;strong&gt;, &lt;positive&gt;, …) and decodes entities.</summary>
    internal static string? StripMarkup(string? value) =>
        value is null ? null : WebUtility.HtmlDecode(MarkupRegex().Replace(value, "")).Trim();

    private async Task<Result<string>> GetAsync(
        string apiToken,
        ProviderEnvironment environment,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(environment)}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        // Factory-created clients are pooled by the factory — do not dispose here.
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<string>(
                    $"Wise API returned {(int)response.StatusCode} {response.StatusCode} for {path}."
                );
            }
            return Result.Ok(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<string>($"Wise API unreachable: {exception.Message}");
        }
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex MarkupRegex();
}
