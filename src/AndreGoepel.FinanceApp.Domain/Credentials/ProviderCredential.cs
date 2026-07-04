namespace AndreGoepel.FinanceApp.Domain.Credentials;

/// <summary>
/// One provider secret (Wise token, Enable Banking key, Claude API key, …),
/// stored DataProtection-encrypted with a per-credential purpose string. The
/// payload is never logged and never written to config files; the key ring
/// persistence comes from app-foundation.
/// </summary>
public sealed class ProviderCredential
{
    /// <summary>Credential key (see <see cref="CredentialKeys"/>) — the document identity.</summary>
    public required string Id { get; init; }

    public required string ProtectedPayload { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RotatedAt { get; set; }
}

/// <summary>Well-known credential keys.</summary>
public static class CredentialKeys
{
    public const string ClaudeApiKey = "claude-api-key";
}
