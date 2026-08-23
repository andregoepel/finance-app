using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Imports;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// HTTP implementation of <see cref="IEnableBankingClient"/>. Every call carries
/// an RS256 JWT built by <see cref="EnableBankingJwtFactory"/> from the app id +
/// private key in the credential store.
/// </summary>
internal sealed class EnableBankingClient(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore,
    TimeProvider timeProvider,
    ILogger<EnableBankingClient> logger
) : IEnableBankingClient
{
    internal const string HttpClientName = "enablebanking";

    /// <summary>
    /// Ceiling on <c>continuation_key</c> follow-ups in one fetch, mirroring
    /// <c>WiseApiClient.MaxActivityPages</c>. An ASPSP that returns a repeating key would
    /// otherwise spin forever inside the Quartz sync job, with no request ever failing to
    /// break the loop.
    /// </summary>
    private const int MaxTransactionPages = 20;

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

    public async Task<Result<EnableBankingFetch>> GetTransactionsAsync(
        string accountUid,
        DateOnly from,
        CancellationToken cancellationToken = default
    )
    {
        var transactions = new List<EnableBankingTransaction>();
        var errors = new List<ImportRowError>();
        string? continuationKey = null;
        var page = 0;
        var rawEntries = 0;

        do
        {
            page++;
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
                return Result.Fail<EnableBankingFetch>(response.Error!);
            }

            var root = JsonNode.Parse(response.Value!);
            foreach (var entry in root?["transactions"] as JsonArray ?? [])
            {
                rawEntries++;
                if (TryParseTransaction(entry, out var transaction))
                {
                    transactions.Add(transaction);
                }
                else
                {
                    var missing =
                        entry?["booking_date"] is null ? "booking_date"
                        : entry?["transaction_amount"]?["amount"] is null
                            ? "transaction_amount.amount"
                        : "an unparseable transaction_amount.amount";

                    // Reported, not just logged: IProviderConnector promises never to drop a row
                    // silently, and these surface as problem rows on the import batch. English on
                    // purpose — ImportRowError is persisted and rendered long afterwards, so
                    // localizing at write time would freeze whichever culture was active.
                    errors.Add(
                        new ImportRowError(
                            rawEntries,
                            $"Enable Banking entry skipped: {missing} was missing or unreadable.",
                            Truncate(entry?.ToJsonString() ?? "")
                        )
                    );
                    logger.LogWarning(
                        "Skipped an Enable Banking entry for account {AccountRef} on page {Page}: "
                            + "{MissingField}.",
                        Fingerprint(accountUid),
                        page,
                        missing
                    );
                }
            }
            continuationKey = root?["continuation_key"]?.GetValue<string>();

            if (page >= MaxTransactionPages && !string.IsNullOrEmpty(continuationKey))
            {
                logger.LogWarning(
                    "Stopped paging Enable Banking transactions for account {AccountRef} at the "
                        + "{MaxPages}-page cap with a continuation key still outstanding; the "
                        + "result may be incomplete.",
                    Fingerprint(accountUid),
                    MaxTransactionPages
                );
                break;
            }
        } while (!string.IsNullOrEmpty(continuationKey));

        logger.LogInformation(
            "Enable Banking transactions for account {AccountRef} since {DateFrom}: {Pages} "
                + "page(s), {RawEntries} raw entries, {Parsed} parsed, {Skipped} skipped.",
            Fingerprint(accountUid),
            from,
            page,
            rawEntries,
            transactions.Count,
            errors.Count
        );

        return Result.Ok(new EnableBankingFetch(transactions, errors));
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
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug(
                "Enable Banking {Method} {Path} -> {StatusCode} in {ElapsedMs}ms, {Bytes} bytes.",
                method.Method,
                Redact(path),
                (int)response.StatusCode,
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                content.Length
            );
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
            logger.LogWarning(
                exception,
                "Enable Banking {Method} {Path} failed after {ElapsedMs}ms.",
                method.Method,
                Redact(path),
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            return Result.Fail<string>($"Enable Banking API unreachable: {exception.Message}");
        }
    }

    /// <summary>
    /// Replaces the session-specific account uid in a request path with its fingerprint, so a
    /// log line stays correlatable without carrying a live handle to someone's bank account.
    /// </summary>
    private static string Redact(string path)
    {
        const string prefix = "accounts/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return path;
        }

        var rest = path[prefix.Length..];
        var uidLength = rest.IndexOfAny(['/', '?']);
        if (uidLength < 0)
        {
            uidLength = rest.Length;
        }

        return prefix + Fingerprint(rest[..uidLength]) + rest[uidLength..];
    }

    /// <summary>
    /// Short, stable, non-reversible stand-in for an identifier, so the same account can be
    /// followed across log lines without the identifier itself appearing in them.
    /// </summary>
    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();

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
