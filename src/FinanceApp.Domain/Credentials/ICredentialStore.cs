namespace FinanceApp.Domain.Credentials;

/// <summary>
/// Encrypted provider credential storage. Secrets never round-trip to the UI —
/// <see cref="GetInfoAsync"/> exposes only metadata.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Decrypted secret, or <c>null</c> when no credential is stored.</summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    Task<CredentialInfo?> GetInfoAsync(string key, CancellationToken cancellationToken = default);

    Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default);
}

public sealed record CredentialInfo(
    string Key,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt
);
