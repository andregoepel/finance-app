using AndreGoepel.Marten.Configuration;
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
        var protector = Protector(key);

        if (existing is null)
        {
            // Unlike a settings form's "leave blank to keep the current secret" field,
            // ProviderCredential has no "empty credential" state — a document only ever
            // exists once there is a real secret to protect (an absent credential is
            // represented by no document at all, see GetInfoAsync). So the "neither new
            // nor existing" null ProtectOrKeepExisting can return is a caller error here,
            // not a legitimate state to persist.
            var protectedPayload =
                protector.ProtectOrKeepExisting(secret, existingCiphertext: null)
                ?? throw new ArgumentException("A secret is required.", nameof(secret));
            session.Store(new ProviderCredential { Id = key, ProtectedPayload = protectedPayload });
        }
        else
        {
            existing.ProtectedPayload = protector.ProtectOrKeepExisting(
                secret,
                existing.ProtectedPayload
            )!;
            existing.RotatedAt = DateTimeOffset.UtcNow;
            session.Store(existing);
        }
        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Per-credential purpose string, isolating payloads from each other.</summary>
    private IDataProtector Protector(string key) =>
        dataProtectionProvider.CreateProtector($"AndreGoepel.FinanceApp.ProviderCredential.{key}");
}
