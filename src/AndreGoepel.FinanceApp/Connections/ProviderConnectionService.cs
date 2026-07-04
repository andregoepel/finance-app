using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>Implements <see cref="IProviderConnectionService"/> over Marten + the Enable Banking client.</summary>
internal sealed class ProviderConnectionService(
    IDocumentSession session,
    IEnableBankingClient enableBankingClient,
    ICredentialStore credentialStore
) : IProviderConnectionService
{
    /// <summary>Requested AIS consent window; ASPSPs cap this (PSD2 is ~90 days).</summary>
    private const int ConsentValidityDays = 90;
    private const string PsuType = "personal";

    public async Task<IReadOnlyList<ProviderConnection>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => await session.Query<ProviderConnection>().ToListAsync(cancellationToken);

    public async Task<Result<ProviderConnection>> CreateAsync(
        ProviderKind provider,
        string label,
        Guid? ownerUserId,
        ProviderEnvironment environment,
        string? aspspName,
        string? aspspCountry,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Fail<ProviderConnection>("A connection label is required.");
        }

        var connection = new ProviderConnection
        {
            Provider = provider,
            Label = label.Trim(),
            OwnerUserId = ownerUserId,
            Environment = environment,
            AspspName = string.IsNullOrWhiteSpace(aspspName) ? null : aspspName.Trim(),
            AspspCountry = string.IsNullOrWhiteSpace(aspspCountry) ? null : aspspCountry.Trim(),
        };
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(connection);
    }

    public async Task<Result> SetEnvironmentAsync(
        Guid connectionId,
        ProviderEnvironment environment,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await session.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Result.Fail("Connection not found.");
        }
        connection.Environment = environment;
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        // Accounts keep their (now dangling) ConnectionId — surfaced in the UI so
        // they can be re-linked; the encrypted Wise token is left in place (the
        // credential store is append/rotate-only) and simply becomes unreachable.
        session.Delete<ProviderConnection>(connectionId);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> SaveWiseTokenAsync(
        Guid connectionId,
        string token,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await session.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Result.Fail("Connection not found.");
        }
        if (connection.Provider != ProviderKind.Wise)
        {
            return Result.Fail("Only Wise connections use an API token.");
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail("The Wise API token must not be empty.");
        }

        await credentialStore.SaveSecretAsync(
            CredentialKeys.WiseApiToken(connectionId),
            token.Trim(),
            cancellationToken
        );
        return Result.Ok();
    }

    public async Task<Result<string>> StartConsentAsync(
        Guid connectionId,
        string redirectUri,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await session.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Result.Fail<string>("Connection not found.");
        }
        if (!connection.UsesEnableBanking)
        {
            return Result.Fail<string>(
                $"{connection.Provider} does not use Enable Banking consent."
            );
        }
        if (
            string.IsNullOrWhiteSpace(connection.AspspName)
            || string.IsNullOrWhiteSpace(connection.AspspCountry)
        )
        {
            return Result.Fail<string>(
                "Set the ASPSP (bank) name and country on the connection before connecting."
            );
        }

        // We generate and persist the CSRF state; the bank echoes it to the callback.
        var state = Guid.NewGuid().ToString("N");
        var request = new EnableBankingAuthRequest(
            connection.AspspName,
            connection.AspspCountry,
            redirectUri,
            state,
            DateTimeOffset.UtcNow.AddDays(ConsentValidityDays),
            PsuType
        );

        var start = await enableBankingClient.StartAuthorizationAsync(request, cancellationToken);
        if (start.IsFailure)
        {
            return Result.Fail<string>(start.Error!);
        }

        connection.ConsentStatus = ConsentStatus.Pending;
        connection.PendingState = state;
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);

        return Result.Ok(start.Value!.AuthorizationUrl);
    }

    public async Task<Result<ProviderConnection>> CompleteConsentAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await session
            .Query<ProviderConnection>()
            .Where(c => c.PendingState == state)
            .FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return Result.Fail<ProviderConnection>(
                "No matching pending consent — the authorization state did not match (possible CSRF)."
            );
        }

        var authorized = await enableBankingClient.AuthorizeSessionAsync(code, cancellationToken);
        if (authorized.IsFailure)
        {
            return Result.Fail<ProviderConnection>(authorized.Error!);
        }

        connection.ConsentStatus = ConsentStatus.Authorized;
        connection.SessionId = authorized.Value!.SessionId;
        connection.ConsentExpiresAt = authorized.Value.ExpiresAt;
        connection.ConsentAuthorizedAt = DateTimeOffset.UtcNow;
        connection.LinkedAccounts =
        [
            .. authorized.Value.Accounts.Select(a => new EnableBankingLinkedAccount(
                a.Uid,
                a.IdentificationHash,
                a.Iban,
                a.Name,
                a.Currency
            )),
        ];
        connection.PendingState = null;
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);

        return Result.Ok(connection);
    }
}
