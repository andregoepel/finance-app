using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// A bank/crypto account owned by the household. Both users see all accounts;
/// the owning users drive per-person filtering in the UI.
/// </summary>
public sealed class Account
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public required ProviderKind Provider { get; init; }

    public required AccountType Type { get; set; }

    /// <summary>Primary currency (ISO 4217); multi-currency balances arrive per transaction.</summary>
    public required string Currency { get; set; }

    /// <summary>
    /// Identity ids of the household users this account belongs to. A non-shared
    /// account has exactly one; a shared account lists every participating user.
    /// An account belongs to a person P when this contains P's id.
    /// </summary>
    public List<Guid> OwnerUserIds { get; set; } = [];

    /// <summary>True when the account is jointly held by several household users.</summary>
    public bool IsShared { get; set; }

    public required SyncMethod SyncMethod { get; set; }

    public string? Iban { get; set; }

    /// <summary>Provider-side account identifier (set up in Phase 3 for API sync).</summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The provider login this account is synced through (a Wise profile or a bank
    /// consent). One connection owns several accounts; API sync needs it to resolve
    /// the right token/session. <c>null</c> for CSV-only accounts.
    /// </summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>
    /// Enable Banking's stable per-account hash. Its account ids are
    /// session-specific and rotate on every re-consent, so an account is matched
    /// to a synced balance via this hash — never the session id.
    /// </summary>
    public string? IdentificationHash { get; set; }

    /// <summary>
    /// Latest known balance in the account's own currency — the net-worth anchor.
    /// Set by API sync (Wise) or entered manually. <c>decimal</c> per the money rule.
    /// </summary>
    public decimal? CurrentBalance { get; set; }

    /// <summary>
    /// <see cref="CurrentBalance"/> converted to EUR at <see cref="BalanceUpdatedAt"/>.
    /// Net-worth history reconstructs each account's EUR balance from this anchor
    /// plus/minus the EUR transactions around the anchor date.
    /// </summary>
    public decimal? CurrentBalanceEur { get; set; }

    /// <summary>When the balance was last set — the "as of" date of the anchor.</summary>
    public DateTimeOffset? BalanceUpdatedAt { get; set; }

    /// <summary>
    /// Deactivated accounts are hidden from selection lists (import, new-account
    /// owner pickers) but keep all their transactions and history. Reversible via
    /// reactivation; permanent deletion is a separate, guarded action.
    /// </summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>When the account was deactivated; <c>null</c> while active.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    public bool IsActive => Status == AccountStatus.Active;
}

public enum AccountType
{
    Checking,
    CreditCard,
    Crypto,
    MultiCurrency,

    /// <summary>Physical cash, maintained by hand. Appended last: Marten stores enums as integers.</summary>
    Cash,
}

public enum AccountStatus
{
    Active,
    Deactivated,
}

public enum SyncMethod
{
    CsvUpload,
    Api,

    /// <summary>No import at all — entries are typed in by hand (cash). Appended last: Marten stores enums as integers.</summary>
    Manual,
}
