using AndreGoepel.FinanceApp.Domain.Credentials;
using Marten;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Domain.Tests.Credentials;

public class MartenCredentialStoreTests
{
    private readonly IDocumentStore store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession session = Substitute.For<IDocumentSession>();
    private readonly IQuerySession querySession = Substitute.For<IQuerySession>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();
    private readonly MartenCredentialStore credentialStore;

    public MartenCredentialStoreTests()
    {
        store.LightweightSession().Returns(session);
        store.QuerySession().Returns(querySession);
        credentialStore = new MartenCredentialStore(store, dataProtection);
    }

    [Fact]
    public async Task SaveSecretAsync_NewCredential_StoresProtectedPayload()
    {
        // Arrange
        ProviderCredential? stored = null;
        session.Store(Arg.Do<ProviderCredential[]>(credentials => stored = credentials.Single()));

        // Act
        await credentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "sk-ant-secret");

        // Assert
        Assert.NotNull(stored);
        Assert.NotEqual("sk-ant-secret", stored.ProtectedPayload);
        Assert.Equal(
            "sk-ant-secret",
            dataProtection
                .CreateProtector($"AndreGoepel.FinanceApp.ProviderCredential.{CredentialKeys.ClaudeApiKey}")
                .Unprotect(stored.ProtectedPayload)
        );
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSecretAsync_ExistingCredential_RotatesPayload()
    {
        // Arrange
        var existing = new ProviderCredential
        {
            Id = CredentialKeys.ClaudeApiKey,
            ProtectedPayload = "old",
        };
        session
            .LoadAsync<ProviderCredential>(
                CredentialKeys.ClaudeApiKey,
                Arg.Any<CancellationToken>()
            )
            .Returns(existing);

        // Act
        await credentialStore.SaveSecretAsync(CredentialKeys.ClaudeApiKey, "new-secret");

        // Assert
        Assert.NotNull(existing.RotatedAt);
        Assert.NotEqual("old", existing.ProtectedPayload);
    }

    [Fact]
    public async Task GetSecretAsync_MissingCredential_ReturnsNull()
    {
        // Act + Assert
        Assert.Null(await credentialStore.GetSecretAsync("unknown"));
    }

    [Fact]
    public async Task GetSecretAsync_StoredCredential_ReturnsDecryptedSecret()
    {
        // Arrange
        var protector = dataProtection.CreateProtector(
            $"AndreGoepel.FinanceApp.ProviderCredential.{CredentialKeys.ClaudeApiKey}"
        );
        querySession
            .LoadAsync<ProviderCredential>(
                CredentialKeys.ClaudeApiKey,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProviderCredential
                {
                    Id = CredentialKeys.ClaudeApiKey,
                    ProtectedPayload = protector.Protect("sk-ant-secret"),
                }
            );

        // Act
        var secret = await credentialStore.GetSecretAsync(CredentialKeys.ClaudeApiKey);

        // Assert
        Assert.Equal("sk-ant-secret", secret);
    }

    [Fact]
    public async Task GetInfoAsync_NeverExposesThePayload()
    {
        // Arrange
        querySession
            .LoadAsync<ProviderCredential>(
                CredentialKeys.ClaudeApiKey,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProviderCredential
                {
                    Id = CredentialKeys.ClaudeApiKey,
                    ProtectedPayload = "protected",
                }
            );

        // Act
        var info = await credentialStore.GetInfoAsync(CredentialKeys.ClaudeApiKey);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(CredentialKeys.ClaudeApiKey, info.Key);
    }
}
