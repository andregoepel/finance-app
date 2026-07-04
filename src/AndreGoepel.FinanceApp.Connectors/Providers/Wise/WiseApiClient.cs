using System.Net.Http.Headers;
using System.Text.Json;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Thin HTTP client over the Wise personal API. Only the token-only reads this
/// app needs are implemented — profiles and balances (net worth). Statement
/// reads require SCA request signing and are out of scope. The token and
/// environment are passed per call so no secret or base URL is held in a field.
/// </summary>
public interface IWiseApiClient
{
    /// <summary>Wise profiles visible to the token (usually one personal profile).</summary>
    Task<Result<IReadOnlyList<WiseProfile>>> GetProfilesAsync(
        string apiToken,
        ProviderEnvironment environment,
        CancellationToken cancellationToken = default
    );

    /// <summary>Standard balances held under a profile, with their current amounts.</summary>
    Task<Result<IReadOnlyList<WiseBalance>>> GetBalancesAsync(
        string apiToken,
        ProviderEnvironment environment,
        long profileId,
        CancellationToken cancellationToken = default
    );
}

public sealed record WiseProfile(long Id, string Type);

/// <summary><paramref name="Id"/> is the Wise balance id used to link an account (its external id).</summary>
public sealed record WiseBalance(long Id, string Currency, decimal Amount);

internal sealed class WiseApiClient(IHttpClientFactory httpClientFactory) : IWiseApiClient
{
    internal const string HttpClientName = "wise";

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
            $"/v4/profiles/{profileId}/balances?types=STANDARD",
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
                    b.GetProperty("amount").GetProperty("value").GetDecimal()
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
}
