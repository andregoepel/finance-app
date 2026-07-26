using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Credentials;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// HTTP implementation of <see cref="IEnableBankingClient"/>. Every call carries
/// an RS256 JWT built by <see cref="EnableBankingJwtFactory"/> from the app id +
/// private key in the credential store.
/// </summary>
internal sealed class EnableBankingClient(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore,
    TimeProvider timeProvider
) : IEnableBankingClient
{
    internal const string HttpClientName = "enablebanking";

    public async Task<Result<AuthorizationStart>> StartAuthorizationAsync(
        EnableBankingAuthRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var body = new JsonObject
        {
            ["access"] = new JsonObject
            {
                ["valid_until"] = request
                    .ValidUntil.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz"),
            },
            ["aspsp"] = new JsonObject
            {
                ["name"] = request.AspspName,
                ["country"] = request.AspspCountry,
            },
            ["state"] = request.State,
            ["redirect_url"] = request.RedirectUrl,
            ["psu_type"] = request.PsuType,
        };

        var response = await SendAsync(HttpMethod.Post, "auth", body, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Fail<AuthorizationStart>(response.Error!);
        }

        var root = JsonNode.Parse(response.Value!);
        var url = root?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(url))
        {
            return Result.Fail<AuthorizationStart>(
                "Enable Banking /auth returned no authorization url."
            );
        }
        return Result.Ok(
            new AuthorizationStart(url, root?["authorization_id"]?.GetValue<string>() ?? "")
        );
    }

    public async Task<Result<AuthorizedSession>> AuthorizeSessionAsync(
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "sessions",
            new JsonObject { ["code"] = code },
            cancellationToken
        );
        if (response.IsFailure)
        {
            return Result.Fail<AuthorizedSession>(response.Error!);
        }

        var root = JsonNode.Parse(response.Value!);
        var sessionId = root?["session_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionId))
        {
            return Result.Fail<AuthorizedSession>(
                "Enable Banking /sessions returned no session id."
            );
        }

        var accounts =
            (root?["accounts"] as JsonArray)
                ?.Select(a => new EnableBankingSessionAccount(
                    a?["uid"]?.GetValue<string>() ?? "",
                    a?["identification_hash"]?.GetValue<string>() ?? "",
                    a?["account_id"]?["iban"]?.GetValue<string>(),
                    a?["name"]?.GetValue<string>(),
                    a?["currency"]?.GetValue<string>() ?? ""
                ))
                .Where(a => !string.IsNullOrEmpty(a.Uid))
                .ToList()
            ?? [];

        var validUntil = ParseDate(root?["access"]?["valid_until"]) ?? DateTimeOffset.UtcNow;

        return Result.Ok(new AuthorizedSession(sessionId, validUntil, accounts));
    }

    public async Task<Result<IReadOnlyList<EnableBankingTransaction>>> GetTransactionsAsync(
        string accountUid,
        DateOnly from,
        CancellationToken cancellationToken = default
    )
    {
        var transactions = new List<EnableBankingTransaction>();
        string? continuationKey = null;

        do
        {
            var query =
                $"accounts/{accountUid}/transactions?date_from={from:yyyy-MM-dd}"
                + (
                    continuationKey is null
                        ? ""
                        : $"&continuation_key={Uri.EscapeDataString(continuationKey)}"
                );
            var response = await SendAsync(HttpMethod.Get, query, body: null, cancellationToken);
            if (response.IsFailure)
            {
                return Result.Fail<IReadOnlyList<EnableBankingTransaction>>(response.Error!);
            }

            var root = JsonNode.Parse(response.Value!);
            foreach (var entry in root?["transactions"] as JsonArray ?? [])
            {
                if (TryParseTransaction(entry, out var transaction))
                {
                    transactions.Add(transaction);
                }
            }
            continuationKey = root?["continuation_key"]?.GetValue<string>();
        } while (!string.IsNullOrEmpty(continuationKey));

        return Result.Ok<IReadOnlyList<EnableBankingTransaction>>(transactions);
    }

    internal static bool TryParseTransaction(
        JsonNode? entry,
        out EnableBankingTransaction transaction
    )
    {
        transaction = null!;
        var bookingDate = ParseDateOnly(entry?["booking_date"]);
        var amountText = entry?["transaction_amount"]?["amount"]?.GetValue<string>();
        if (
            bookingDate is null
            || !decimal.TryParse(
                amountText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount
            )
        )
        {
            return false;
        }

        var remittance =
            (entry?["remittance_information"] as JsonArray)
                ?.Select(r => r?.GetValue<string>() ?? "")
                .Where(r => r.Length > 0)
                .ToList()
            ?? [];

        transaction = new EnableBankingTransaction(
            entry?["entry_reference"]?.GetValue<string>(),
            bookingDate.Value,
            ParseDateOnly(entry?["value_date"]),
            amount,
            entry?["transaction_amount"]?["currency"]?.GetValue<string>() ?? "",
            entry?["credit_debit_indicator"]?.GetValue<string>() ?? "",
            entry?["creditor"]?["name"]?.GetValue<string>(),
            entry?["debtor"]?["name"]?.GetValue<string>(),
            remittance,
            entry?["status"]?.GetValue<string>() ?? "",
            entry?.ToJsonString() ?? ""
        );
        return true;
    }

    /// <summary>Issues an authenticated request and returns the body, or a failure Result.</summary>
    private async Task<Result<string>> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken cancellationToken
    )
    {
        var token = await BuildBearerTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return Result.Fail<string>(token.Error!);
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value!);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<string>(
                    $"Enable Banking API returned {(int)response.StatusCode} for {path}: {Truncate(content)}"
                );
            }
            return Result.Ok(content);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<string>($"Enable Banking API unreachable: {exception.Message}");
        }
    }

    private async Task<Result<string>> BuildBearerTokenAsync(CancellationToken cancellationToken)
    {
        var applicationId = await credentialStore.GetSecretAsync(
            CredentialKeys.EnableBankingApplicationId,
            cancellationToken
        );
        var privateKey = await credentialStore.GetSecretAsync(
            CredentialKeys.EnableBankingPrivateKey,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(privateKey))
        {
            return Result.Fail<string>(
                "Enable Banking application id / private key are not configured (Settings → Connections)."
            );
        }

        return Result.Ok(
            EnableBankingJwtFactory.Create(
                applicationId,
                privateKey,
                timeProvider.GetUtcNow(),
                TimeSpan.FromMinutes(5)
            )
        );
    }

    private static DateTimeOffset? ParseDate(JsonNode? node) =>
        DateTimeOffset.TryParse(
            node?.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var value
        )
            ? value
            : null;

    private static DateOnly? ParseDateOnly(JsonNode? node) =>
        DateOnly.TryParse(
            node?.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var value
        )
            ? value
            : null;

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
}
