using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Thin HTTP client over the Wise personal API: profiles and balances (net
/// worth) are token-only reads; balance statements (transactions) additionally
/// answer Wise's SCA challenge by signing the one-time approval token with the
/// connection's registered RSA key. The token, key and environment are passed
/// per call so no secret or base URL is held in a field.
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
    /// Booked transactions of one balance in a date interval, from the
    /// balance-statement endpoint (SCA-protected). <paramref name="scaPrivateKeyPem"/>
    /// answers the 403 challenge; without it the call fails with an actionable
    /// message instead of retrying.
    /// </summary>
    Task<Result<IReadOnlyList<WiseStatementTransaction>>> GetBalanceStatementAsync(
        string apiToken,
        string? scaPrivateKeyPem,
        ProviderEnvironment environment,
        long profileId,
        long balanceId,
        DateOnly intervalStart,
        DateOnly intervalEnd,
        CancellationToken cancellationToken = default
    );
}

public sealed record WiseProfile(long Id, string Type);

/// <summary><paramref name="Id"/> is the Wise balance id used to link an account (its external id).</summary>
public sealed record WiseBalance(long Id, string Currency, decimal Amount);

/// <summary>
/// One line of a Wise balance statement. <paramref name="Amount"/> is the signed
/// balance impact (negative for debits); <paramref name="ReferenceNumber"/> is
/// Wise's stable per-transaction reference used as the external id.
/// </summary>
public sealed record WiseStatementTransaction(
    DateOnly Date,
    decimal Amount,
    string Currency,
    string? Description,
    string? Counterparty,
    string? ReferenceNumber,
    string RawJson
);
