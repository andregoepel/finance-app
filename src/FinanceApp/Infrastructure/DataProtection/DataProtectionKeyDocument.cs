namespace FinanceApp.Infrastructure.DataProtection;

/// <summary>
/// Marten document holding one DataProtection key ring entry. The key ring must
/// survive container rebuilds — encrypted provider credentials are unrecoverable
/// without it — so it is persisted in Postgres alongside the rest of the data.
/// </summary>
public sealed class DataProtectionKeyDocument
{
    public required string Id { get; init; }

    public required string Xml { get; init; }
}
