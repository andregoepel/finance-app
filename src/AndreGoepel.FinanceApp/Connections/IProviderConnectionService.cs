using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

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
        string? aspspName,
        string? aspspCountry,
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

    /// <summary>
    /// Stores/rotates the RSA private key (PEM) answering Wise's SCA challenge on
    /// statement reads (encrypted). The public half is registered on the Wise account.
    /// </summary>
    Task<Result> SaveWiseScaKeyAsync(
        Guid connectionId,
        string privateKeyPem,
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
