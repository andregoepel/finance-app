using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>
/// Manages provider connections (a login at a provider, owned by a household
/// user) and their secrets: create/list/delete connections, store the Wise token
/// per connection, and drive the Enable Banking consent lifecycle (start → bank
/// redirect → callback completion). One connection owns many accounts, so
/// credentials live here, not per account.
/// </summary>
public interface IProviderConnectionService
{
    Task<IReadOnlyList<ProviderConnection>> GetAllAsync(
        CancellationToken cancellationToken = default
    );

    Task<Result<ProviderConnection>> CreateAsync(
        ProviderKind provider,
        string label,
        Guid? ownerUserId,
        ProviderEnvironment environment,
        CancellationToken cancellationToken = default
    );

    Task<Result> DeleteAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>Switches a connection between sandbox and production (Wise has separate tokens per env).</summary>
    Task<Result> SetEnvironmentAsync(
        Guid connectionId,
        ProviderEnvironment environment,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores/rotates the Wise API token for a Wise connection (encrypted).</summary>
    Task<Result> SaveWiseTokenAsync(
        Guid connectionId,
        string token,
        CancellationToken cancellationToken = default
    );

    /// <summary>Starts an Enable Banking consent; returns the bank authorization URL.</summary>
    Task<Result<string>> StartConsentAsync(
        Guid connectionId,
        string redirectUri,
        CancellationToken cancellationToken = default
    );

    /// <summary>Completes a consent from the callback code + state (CSRF-checked).</summary>
    Task<Result<ProviderConnection>> CompleteConsentAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ProviderConnectionService(
    IDocumentSession session,
    IEnableBankingClient enableBankingClient,
    ICredentialStore credentialStore
) : IProviderConnectionService
{
    public async Task<IReadOnlyList<ProviderConnection>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => await session.Query<ProviderConnection>().ToListAsync(cancellationToken);

    public async Task<Result<ProviderConnection>> CreateAsync(
        ProviderKind provider,
        string label,
        Guid? ownerUserId,
        ProviderEnvironment environment,
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

        var start = await enableBankingClient.StartAuthorizationAsync(
            connection.Provider,
            redirectUri,
            cancellationToken
        );
        if (start.IsFailure)
        {
            return Result.Fail<string>(start.Error!);
        }

        connection.ConsentStatus = ConsentStatus.Pending;
        connection.PendingState = start.Value!.State;
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);

        return Result.Ok(start.Value.AuthorizationUrl);
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
        connection.LinkedAccounts = [.. authorized.Value.Accounts];
        connection.PendingState = null;
        session.Store(connection);
        await session.SaveChangesAsync(cancellationToken);

        return Result.Ok(connection);
    }
}
