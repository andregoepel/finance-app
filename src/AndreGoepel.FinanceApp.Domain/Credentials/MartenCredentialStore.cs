using Marten;
using Microsoft.AspNetCore.DataProtection;

namespace AndreGoepel.FinanceApp.Domain.Credentials;

internal sealed class MartenCredentialStore(
    IDocumentStore store,
    IDataProtectionProvider dataProtectionProvider
) : ICredentialStore
{
    public async Task<string?> GetSecretAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.QuerySession();
        var credential = await session.LoadAsync<ProviderCredential>(key, cancellationToken);
        return credential is null ? null : Protector(key).Unprotect(credential.ProtectedPayload);
    }

    public async Task<CredentialInfo?> GetInfoAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.QuerySession();
        var credential = await session.LoadAsync<ProviderCredential>(key, cancellationToken);
        return credential is null
            ? null
            : new CredentialInfo(credential.Id, credential.CreatedAt, credential.RotatedAt);
    }

    public async Task SaveSecretAsync(
        string key,
        string secret,
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.LightweightSession();
        var existing = await session.LoadAsync<ProviderCredential>(key, cancellationToken);
        if (existing is null)
        {
            session.Store(
                new ProviderCredential
                {
                    Id = key,
                    ProtectedPayload = Protector(key).Protect(secret),
                }
            );
        }
        else
        {
            existing.ProtectedPayload = Protector(key).Protect(secret);
            existing.RotatedAt = DateTimeOffset.UtcNow;
            session.Store(existing);
        }
        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Per-credential purpose string, isolating payloads from each other.</summary>
    private IDataProtector Protector(string key) =>
        dataProtectionProvider.CreateProtector($"FinanceApp.ProviderCredential.{key}");
}
