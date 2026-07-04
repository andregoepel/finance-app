using FinanceApp.Domain.Credentials;
using NSubstitute;

namespace FinanceApp.Domain.Tests.Credentials;

public class SaveProviderCredentialCommandHandlerTests
{
    private readonly ICredentialStore store = Substitute.For<ICredentialStore>();

    [Fact]
    public async Task Handle_ValidSecret_SavesTrimmed()
    {
        // Act
        var result = await SaveProviderCredentialCommandHandler.Handle(
            new SaveProviderCredentialCommand(CredentialKeys.ClaudeApiKey, "  sk-ant-x  "),
            store,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsSuccess);
        await store
            .Received(1)
            .SaveSecretAsync(CredentialKeys.ClaudeApiKey, "sk-ant-x", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("key", "")]
    [InlineData("key", "   ")]
    public async Task Handle_MissingKeyOrSecret_Fails(string key, string secret)
    {
        // Act
        var result = await SaveProviderCredentialCommandHandler.Handle(
            new SaveProviderCredentialCommand(key, secret),
            store,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        await store
            .DidNotReceive()
            .SaveSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
