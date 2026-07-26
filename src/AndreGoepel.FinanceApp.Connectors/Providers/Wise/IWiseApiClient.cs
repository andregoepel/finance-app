using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Thin HTTP client over the Wise personal API. Everything is a token-only read:
/// profiles, balances (net worth) and the activity feed (transaction history).
/// Personal accounts cannot register SCA public keys anymore, so the SCA-protected
/// statement endpoints are off-limits — activities are the transaction source.
/// The token and environment are passed per call so no secret or base URL is
/// held in a field.
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

    /// <summary>
    /// Monetary activities of a profile in a time window (newest first, all
    /// currencies — the caller filters). Follows cursor pagination until the
    /// window is exhausted.
    /// </summary>
    Task<Result<IReadOnlyList<WiseActivity>>> GetActivitiesAsync(
        string apiToken,
        ProviderEnvironment environment,
        long profileId,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default
    );
}

public sealed record WiseProfile(long Id, string Type);

/// <summary><paramref name="Id"/> is the Wise balance id used to link an account (its external id).</summary>
public sealed record WiseBalance(long Id, string Currency, decimal Amount);

/// <summary>
/// One entry of the Wise activity feed. <paramref name="Amount"/> is the signed
/// balance impact parsed from the primary display amount (credits are marked up
/// by Wise, everything else is money out). Balance conversions (INTERBALANCE)
/// carry both sides: the primary amount is the target currency received, the
/// secondary amount (<paramref name="SecondaryAmount"/>/<paramref name="SecondaryCurrency"/>)
/// is the source currency spent. <paramref name="Id"/> is the stable activity id
/// used for dedup; <paramref name="Status"/> lets the caller keep only COMPLETED
/// entries (in-progress ones still change).
/// </summary>
public sealed record WiseActivity(
    string Id,
    string Type,
    string Status,
    DateOnly Date,
    decimal Amount,
    string Currency,
    string? Title,
    string? Description,
    string RawJson,
    decimal? SecondaryAmount = null,
    string? SecondaryCurrency = null
);
