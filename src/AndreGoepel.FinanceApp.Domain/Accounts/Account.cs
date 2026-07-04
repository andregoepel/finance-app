using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// A bank/crypto account owned by the household. Both users see all accounts;
/// the owner tag drives per-person filtering in the UI.
/// </summary>
public sealed class Account
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public required ProviderKind Provider { get; init; }

    public required AccountType Type { get; set; }

    /// <summary>Primary currency (ISO 4217); multi-currency balances arrive per transaction.</summary>
    public required string Currency { get; set; }

    public required AccountOwner Owner { get; set; }

    public required SyncMethod SyncMethod { get; set; }

    public string? Iban { get; set; }

    /// <summary>Provider-side account identifier (set up in Phase 3 for API sync).</summary>
    public string? ExternalId { get; set; }
}

public enum AccountType
{
    Checking,
    CreditCard,
    Crypto,
    MultiCurrency,
}

public enum AccountOwner
{
    Andre,
    Wife,
    Joint,
}

public enum SyncMethod
{
    CsvUpload,
    Api,
}
