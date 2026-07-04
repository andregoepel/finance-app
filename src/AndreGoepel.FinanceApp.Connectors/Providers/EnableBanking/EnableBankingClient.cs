using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// HTTP client for the Enable Banking PSD2 API: start authorization (consent),
/// exchange the returned code for a session, list linked accounts and read
/// transactions. Every call carries an RS256 JWT built by
/// <see cref="EnableBankingJwtFactory"/> from the app id + private key in the
/// credential store. Behind an interface so the connector and consent flow are
/// testable with no network.
/// </summary>
public interface IEnableBankingClient
{
    /// <summary>
    /// Begins a consent: returns the bank's authorization URL to redirect the
    /// user to, plus the <c>state</c> to persist and validate on callback.
    /// </summary>
    Task<Result<AuthorizationStart>> StartAuthorizationAsync(
        ProviderKind provider,
        string redirectUri,
        CancellationToken cancellationToken = default
    );

    /// <summary>Exchanges the callback <c>code</c> for a session + its linked accounts.</summary>
    Task<Result<AuthorizedSession>> AuthorizeSessionAsync(
        string code,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads transactions for one session account over a window (raw JSON).</summary>
    Task<Result<string>> GetTransactionsJsonAsync(
        string sessionAccountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Where to send the user to authorize, plus the CSRF state to persist.</summary>
public sealed record AuthorizationStart(string AuthorizationUrl, string State);

/// <summary>An authorized session with its expiry and the accounts it exposes.</summary>
public sealed record AuthorizedSession(
    string SessionId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<EnableBankingLinkedAccount> Accounts
);

/// <summary>
/// Phase 3 skeleton client. JWT auth wiring and the method surface are in place;
/// request/response bodies are filled in together once the Enable Banking
/// application is registered. The app id + private key are read per call so no
/// secret is held in a field.
/// </summary>
internal sealed class EnableBankingClient(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore,
    TimeProvider timeProvider
) : IEnableBankingClient
{
    internal const string HttpClientName = "enablebanking";

    public async Task<Result<AuthorizationStart>> StartAuthorizationAsync(
        ProviderKind provider,
        string redirectUri,
        CancellationToken cancellationToken = default
    )
    {
        var token = await BuildBearerTokenAsync(cancellationToken);
        return token.IsFailure
            ? Result.Fail<AuthorizationStart>(token.Error!)
            : NotImplemented<AuthorizationStart>();
    }

    public async Task<Result<AuthorizedSession>> AuthorizeSessionAsync(
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var token = await BuildBearerTokenAsync(cancellationToken);
        return token.IsFailure
            ? Result.Fail<AuthorizedSession>(token.Error!)
            : NotImplemented<AuthorizedSession>();
    }

    public async Task<Result<string>> GetTransactionsJsonAsync(
        string sessionAccountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default
    )
    {
        var token = await BuildBearerTokenAsync(cancellationToken);
        return token.IsFailure ? Result.Fail<string>(token.Error!) : NotImplemented<string>();
    }

    /// <summary>Builds the bearer token for a request — the wired-up auth seam.</summary>
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

        _ = httpClientFactory;
        return Result.Ok(
            EnableBankingJwtFactory.Create(
                applicationId,
                privateKey,
                timeProvider.GetUtcNow(),
                TimeSpan.FromMinutes(5)
            )
        );
    }

    // BuildBearerTokenAsync (JWT auth) is the proven half of this client; the HTTP
    // request/response bodies land next, together, against the registered
    // Enable Banking application.
    private static Result<T> NotImplemented<T>() =>
        Result.Fail<T>("Enable Banking client is not implemented yet — Phase 3 next step.");
}
