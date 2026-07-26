using AndreGoepel.FinanceApp.Domain.Credentials;
using Marten;
using Microsoft.AspNetCore.DataProtection;
// AndreGoepel.Marten.Testing (global-used, see GlobalUsings.cs) ships its own
// IntegrationCollection as a copy-paste starting point, which collides by name with this
// project's own — alias the local one instead of a plain namespace `using`.
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Credentials;

/// <summary>
/// Exercises <see cref="MartenCredentialStore"/> against a real Postgres via
/// <see cref="MartenFixture"/> — this store is thin glue over Marten document
/// load/store plus DataProtection, so the real value is confirming the actual
/// round trip through Postgres, not a mocked <see cref="IDocumentStore"/>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MartenCredentialStoreTests(MartenFixture fixture) : IAsyncLifetime
{
    private readonly IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();
    private MartenCredentialStore CredentialStore => new(fixture.Store, dataProtection);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveSecretAsync_NewCredential_StoresProtectedPayload()
    {
        // Act
        await CredentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "sk-ant-secret", Ct);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var stored = await session.LoadAsync<ProviderCredential>(CredentialKeys.ClaudeApiKey, Ct);
        Assert.NotNull(stored);
        Assert.NotEqual("sk-ant-secret", stored.ProtectedPayload);
        Assert.Equal(
            "sk-ant-secret",
            await CredentialStore.GetSecretAsync(CredentialKeys.ClaudeApiKey, Ct)
        );
    }

    [Fact]
    public async Task SaveSecretAsync_ExistingCredential_RotatesPayload()
    {
        // Arrange
        await CredentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "old-secret", Ct);

        // Act
        await CredentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "new-secret", Ct);

        // Assert
        await using var session = fixture.Store.QuerySession();
        var updated = await session.LoadAsync<ProviderCredential>(CredentialKeys.ClaudeApiKey, Ct);
        Assert.NotNull(updated);
        Assert.NotNull(updated.RotatedAt);
        Assert.Equal(
            "new-secret",
            await CredentialStore.GetSecretAsync(CredentialKeys.ClaudeApiKey, Ct)
        );
    }

    [Fact]
    public async Task GetSecretAsync_MissingCredential_ReturnsNull()
    {
        // Act + Assert
        Assert.Null(await CredentialStore.GetSecretAsync("unknown", Ct));
    }

    [Fact]
    public async Task GetSecretAsync_StoredCredential_ReturnsDecryptedSecret()
    {
        // Arrange
        await CredentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "sk-ant-secret", Ct);

        // Act
        var secret = await CredentialStore.GetSecretAsync(CredentialKeys.ClaudeApiKey, Ct);

        // Assert
        Assert.Equal("sk-ant-secret", secret);
    }

    [Fact]
    public async Task GetInfoAsync_NeverExposesThePayload()
    {
        // Arrange
        await CredentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "sk-ant-secret", Ct);

        // Act
        var info = await CredentialStore.GetInfoAsync(CredentialKeys.ClaudeApiKey, Ct);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(CredentialKeys.ClaudeApiKey, info.Key);
    }
}
