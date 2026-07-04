namespace AndreGoepel.FinanceApp.Domain.Providers;

/// <summary>
/// One login at a provider — a Wise personal profile or an Enable Banking
/// consent at one bank — owned by a household user. A connection owns one or more
/// <c>Account</c>s (a Wise profile exposes several balances; a bank consent
/// several accounts), so credentials and consent live here, not per account.
/// Both users can each hold their own connection per provider.
/// </summary>
public sealed class ProviderConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required ProviderKind Provider { get; init; }

    /// <summary>Human label shown in the UI, e.g. "André – Wise".</summary>
    public required string Label { get; set; }

    /// <summary>The household user this login belongs to (their Wise/bank account).</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Which provider environment this login targets. Wise sandbox and production
    /// are separate systems with separate tokens and base URLs; a connection is
    /// bound to one.
    /// </summary>
    public ProviderEnvironment Environment { get; set; } = ProviderEnvironment.Production;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True for providers whose connection is an Enable Banking PSD2 consent.</summary>
    public bool UsesEnableBanking => Provider is ProviderKind.Dkb or ProviderKind.Revolut;

    // ---- Enable Banking consent (unused for Wise, which authenticates with a token) ----

    /// <summary>Enable Banking ASPSP (bank) name to authorize against, e.g. "DKB" or "Mock ASPSP".</summary>
    public string? AspspName { get; set; }

    /// <summary>ISO country of the ASPSP, e.g. "DE".</summary>
    public string? AspspCountry { get; set; }

    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.NotConnected;

    /// <summary>Enable Banking session id from the last authorization (rotates on re-consent).</summary>
    public string? SessionId { get; set; }

    /// <summary>Opaque state sent on the authorize redirect and validated on callback (CSRF).</summary>
    public string? PendingState { get; set; }

    public DateTimeOffset? ConsentAuthorizedAt { get; set; }

    public DateTimeOffset? ConsentExpiresAt { get; set; }

    /// <summary>Accounts exposed by this consent, keyed by their stable identification hash.</summary>
    public List<EnableBankingLinkedAccount> LinkedAccounts { get; set; } = [];

    /// <summary>True once an authorized consent's expiry is in the past (90–180 day PSD2 window).</summary>
    public bool ConsentExpired =>
        ConsentStatus == ConsentStatus.Authorized && ConsentExpiresAt <= DateTimeOffset.UtcNow;
}

/// <summary>
/// A single bank account exposed by an Enable Banking consent. Enable Banking's
/// account ids (<see cref="Uid"/>) are session-specific and rotate on every
/// re-consent, so accounts are matched via the stable
/// <see cref="IdentificationHash"/>; the current <see cref="Uid"/> is what the
/// transactions call needs.
/// </summary>
public sealed record EnableBankingLinkedAccount(
    string Uid,
    string IdentificationHash,
    string? Iban,
    string? Name,
    string Currency
);

public enum ConsentStatus
{
    NotConnected,
    Pending,
    Authorized,
    Expired,
}

public enum ProviderEnvironment
{
    Production,
    Sandbox,
}
