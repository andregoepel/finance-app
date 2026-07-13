using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// HTTP implementation of <see cref="IWiseApiClient"/>. The token and environment
/// are passed per call so no secret or base URL is held in a field.
/// </summary>
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

    public async Task<Result<IReadOnlyList<WiseStatementTransaction>>> GetBalanceStatementAsync(
        string apiToken,
        string? scaPrivateKeyPem,
        ProviderEnvironment environment,
        long profileId,
        long balanceId,
        DateOnly intervalStart,
        DateOnly intervalEnd,
        CancellationToken cancellationToken = default
    )
    {
        var path =
            $"/v1/profiles/{profileId}/balance-statements/{balanceId}/statement.json"
            + $"?intervalStart={intervalStart:yyyy-MM-dd}T00:00:00.000Z"
            + $"&intervalEnd={intervalEnd:yyyy-MM-dd}T23:59:59.999Z"
            + "&type=COMPACT";

        var json = await GetWithScaAsync(
            apiToken,
            scaPrivateKeyPem,
            environment,
            path,
            cancellationToken
        );
        if (json.IsFailure)
        {
            return Result.Fail<IReadOnlyList<WiseStatementTransaction>>(json.Error!);
        }

        try
        {
            return Result.Ok(ParseStatement(json.Value!));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return Result.Fail<IReadOnlyList<WiseStatementTransaction>>(
                $"Unreadable Wise statement response: {exception.Message}"
            );
        }
    }

    /// <summary>Maps the COMPACT statement JSON to statement lines. Internal for fixture tests.</summary>
    internal static IReadOnlyList<WiseStatementTransaction> ParseStatement(string json)
    {
        using var document = JsonDocument.Parse(json);
        var transactions = new List<WiseStatementTransaction>();
        foreach (var item in document.RootElement.GetProperty("transactions").EnumerateArray())
        {
            var amount = item.GetProperty("amount").GetProperty("value").GetDecimal();
            // The statement reports signed values; keep a defensive belt in case a
            // DEBIT ever arrives positive.
            if (
                item.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "DEBIT", StringComparison.OrdinalIgnoreCase)
                && amount > 0
            )
            {
                amount = -amount;
            }

            string? description = null;
            string? counterparty = null;
            if (item.TryGetProperty("details", out var details))
            {
                description = details.TryGetProperty("description", out var desc)
                    ? desc.GetString()
                    : null;
                counterparty = FirstNonEmpty(
                    details.TryGetProperty("merchant", out var merchant)
                    && merchant.ValueKind == JsonValueKind.Object
                    && merchant.TryGetProperty("name", out var merchantName)
                        ? merchantName.GetString()
                        : null,
                    details.TryGetProperty("senderName", out var sender)
                        ? sender.GetString()
                        : null,
                    details.TryGetProperty("recipient", out var recipient)
                    && recipient.ValueKind == JsonValueKind.Object
                    && recipient.TryGetProperty("name", out var recipientName)
                        ? recipientName.GetString()
                        : null
                );
            }

            transactions.Add(
                new WiseStatementTransaction(
                    Date: DateOnly.FromDateTime(
                        item.GetProperty("date").GetDateTimeOffset().UtcDateTime
                    ),
                    Amount: amount,
                    Currency: item.GetProperty("amount").GetProperty("currency").GetString() ?? "",
                    Description: description,
                    Counterparty: counterparty,
                    ReferenceNumber: item.TryGetProperty("referenceNumber", out var reference)
                        ? reference.GetString()
                        : null,
                    RawJson: item.GetRawText()
                )
            );
        }
        return transactions;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private async Task<Result<string>> GetAsync(
        string apiToken,
        ProviderEnvironment environment,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var request = NewGetRequest(apiToken, environment, path);

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

    /// <summary>
    /// GET with Wise's SCA handshake: statement reads answer 403 with a one-time
    /// token in <c>x-2fa-approval</c>; the retry carries that token back plus its
    /// RSA-SHA256 signature in <c>X-Signature</c>. One retry only — a second 403
    /// means the registered public key does not match the private key.
    /// </summary>
    private async Task<Result<string>> GetWithScaAsync(
        string apiToken,
        string? scaPrivateKeyPem,
        ProviderEnvironment environment,
        string path,
        CancellationToken cancellationToken
    )
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            string oneTimeToken;
            using (var request = NewGetRequest(apiToken, environment, path))
            using (var response = await httpClient.SendAsync(request, cancellationToken))
            {
                if (response.IsSuccessStatusCode)
                {
                    return Result.Ok(await response.Content.ReadAsStringAsync(cancellationToken));
                }

                if (
                    response.StatusCode != HttpStatusCode.Forbidden
                    || !response.Headers.TryGetValues("x-2fa-approval", out var approvalValues)
                    || approvalValues.FirstOrDefault() is not { Length: > 0 } token
                )
                {
                    return Result.Fail<string>(
                        $"Wise API returned {(int)response.StatusCode} {response.StatusCode} for {path}."
                    );
                }
                oneTimeToken = token;
            }

            if (string.IsNullOrWhiteSpace(scaPrivateKeyPem))
            {
                return Result.Fail<string>(
                    "Wise statement reads require SCA request signing: register a public key "
                        + "on the Wise account and store the matching private key under "
                        + "Settings → Connections."
                );
            }

            string signature;
            try
            {
                signature = WiseScaSigner.Sign(scaPrivateKeyPem, oneTimeToken);
            }
            catch (Exception exception)
                when (exception is ArgumentException or CryptographicException)
            {
                return Result.Fail<string>(
                    $"The stored Wise SCA private key is not a usable RSA PEM key: {exception.Message}"
                );
            }

            using var signedRequest = NewGetRequest(apiToken, environment, path);
            signedRequest.Headers.Add("x-2fa-approval", oneTimeToken);
            signedRequest.Headers.Add("X-Signature", signature);
            using var signedResponse = await httpClient.SendAsync(signedRequest, cancellationToken);
            if (!signedResponse.IsSuccessStatusCode)
            {
                return Result.Fail<string>(
                    signedResponse.StatusCode == HttpStatusCode.Forbidden
                        ? "Wise rejected the SCA signature — the private key stored here does "
                            + "not match the public key registered on the Wise account."
                        : $"Wise API returned {(int)signedResponse.StatusCode} {signedResponse.StatusCode} for {path}."
                );
            }
            return Result.Ok(await signedResponse.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<string>($"Wise API unreachable: {exception.Message}");
        }
    }

    private static HttpRequestMessage NewGetRequest(
        string apiToken,
        ProviderEnvironment environment,
        string path
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(environment)}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return request;
    }
}
