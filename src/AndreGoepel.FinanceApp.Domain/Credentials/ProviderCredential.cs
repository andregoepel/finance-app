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

    /// <summary>Enable Banking registered application id (the JWT <c>kid</c>) — one per app, global.</summary>
    public const string EnableBankingApplicationId = "enablebanking-application-id";

    /// <summary>PEM-encoded RSA private key that signs Enable Banking JWTs (RS256) — global.</summary>
    public const string EnableBankingPrivateKey = "enablebanking-private-key";

    /// <summary>
    /// Wise personal API token (read scope), scoped to one connection: each Wise
    /// login (profile) has its own token, and one login exposes many balances.
    /// Wise personal accounts cannot register SCA public keys anymore, so the
    /// token is deliberately the only Wise credential — everything the app reads
    /// (profiles, balances, activities) is token-only.
    /// </summary>
    public static string WiseApiToken(Guid connectionId) => $"wise-api-token:{connectionId}";
}
