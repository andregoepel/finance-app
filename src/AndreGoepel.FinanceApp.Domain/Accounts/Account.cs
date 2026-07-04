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
}
