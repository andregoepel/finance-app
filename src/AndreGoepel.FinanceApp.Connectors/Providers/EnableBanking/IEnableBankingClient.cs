using AndreGoepel.FinanceApp.Domain;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// HTTP client for the Enable Banking PSD2 API: start authorization (consent),
/// exchange the returned code for a session and its accounts, and read
/// transactions. Every call carries an RS256 JWT built by
/// <see cref="EnableBankingJwtFactory"/> from the app id + private key in the
/// credential store. Behind an interface so the connector and consent flow are
/// testable with no network.
/// </summary>
public interface IEnableBankingClient
{
    /// <summary>Begins a consent; returns the bank authorization URL to redirect to.</summary>
    Task<Result<AuthorizationStart>> StartAuthorizationAsync(
        EnableBankingAuthRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Exchanges the callback <c>code</c> for a session and its accounts.</summary>
    Task<Result<AuthorizedSession>> AuthorizeSessionAsync(
        string code,
        CancellationToken cancellationToken = default
    );

    /// <summary>All transactions for one session account since a date (pages until exhausted).</summary>
    Task<Result<IReadOnlyList<EnableBankingTransaction>>> GetTransactionsAsync(
        string accountUid,
        DateOnly from,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Inputs for starting a consent: which bank, where to return, and the CSRF state.</summary>
public sealed record EnableBankingAuthRequest(
    string AspspName,
    string AspspCountry,
    string RedirectUrl,
    string State,
    DateTimeOffset ValidUntil,
    string PsuType
);

/// <summary>Where to send the user to authorize, plus the authorization id.</summary>
public sealed record AuthorizationStart(string AuthorizationUrl, string AuthorizationId);

/// <summary>An authorized session with its consent expiry and the accounts it exposes.</summary>
public sealed record AuthorizedSession(
    string SessionId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<EnableBankingSessionAccount> Accounts
);

/// <summary>
/// One account from a session. <see cref="Uid"/> is session-specific (used for the
/// transactions call); <see cref="IdentificationHash"/> is stable across consents
/// (used to match our <c>Account</c>).
/// </summary>
public sealed record EnableBankingSessionAccount(
    string Uid,
    string IdentificationHash,
    string? Iban,
    string? Name,
    string Currency
);

/// <summary>
/// One booked transaction. <see cref="Amount"/> is the raw positive value; the
/// sign is carried by <see cref="CreditDebitIndicator"/> (<c>DBIT</c> = money out).
/// </summary>
public sealed record EnableBankingTransaction(
    string? EntryReference,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    decimal Amount,
    string Currency,
    string CreditDebitIndicator,
    string? CreditorName,
    string? DebtorName,
    IReadOnlyList<string> RemittanceInformation,
    string Status,
    string RawJson
);
