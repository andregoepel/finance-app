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
