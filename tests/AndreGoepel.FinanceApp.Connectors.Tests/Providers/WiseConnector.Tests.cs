using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class WiseConnectorTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static ProviderSyncRequest Request(string? externalId = "306149") =>
        new(
            AccountId: Guid.NewGuid(),
            Provider: ProviderKind.Wise,
            ConnectionId: ConnectionId,
            ExternalId: externalId,
            IdentificationHash: null,
            ProviderAccountReference: null,
            Since: new DateOnly(2026, 6, 1),
            Environment: ProviderEnvironment.Sandbox
        );

    [Fact]
    public void Supports_OnlyWise()
    {
        // Arrange
        var connector = new WiseConnector(
            Substitute.For<IWiseApiClient>(),
            Substitute.For<ICredentialStore>()
        );

        // Act / Assert
        Assert.True(connector.Supports(ProviderKind.Wise));
        Assert.False(connector.Supports(ProviderKind.Dkb));
        Assert.False(connector.Supports(ProviderKind.Revolut));
    }

    [Fact]
    public async Task FetchAsync_HappyPath_ResolvesProfileAndNormalizesRows()
    {
        // Arrange
        var client = Substitute.For<IWiseApiClient>();
        var store = Substitute.For<ICredentialStore>();
        store
            .GetSecretAsync(CredentialKeys.WiseApiToken(ConnectionId), Arg.Any<CancellationToken>())
            .Returns("token");
        store
            .GetSecretAsync(
                CredentialKeys.WiseScaPrivateKey(ConnectionId),
                Arg.Any<CancellationToken>()
            )
            .Returns("PEM");
        client
            .GetProfilesAsync("token", ProviderEnvironment.Sandbox, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<WiseProfile>>([new WiseProfile(42, "personal")]));
        client
            .GetBalancesAsync(
                "token",
                ProviderEnvironment.Sandbox,
                42,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Ok<IReadOnlyList<WiseBalance>>([new WiseBalance(306149, "EUR", 100m)]));
        client
            .GetBalanceStatementAsync(
                "token",
                "PEM",
                ProviderEnvironment.Sandbox,
                42,
                306149,
                new DateOnly(2026, 6, 1),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Ok<IReadOnlyList<WiseStatementTransaction>>([
                    new WiseStatementTransaction(
                        new DateOnly(2026, 7, 3),
                        -25.50m,
                        "EUR",
                        "Card transaction",
                        "REWE Markt",
                        "CARD-1234",
                        "{}"
                    ),
                ])
            );
        var connector = new WiseConnector(client, store);

        // Act
        var result = await connector.FetchAsync(Request());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("wise-api-v1", result.Value!.SyncSource);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(-25.50m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal("REWE Markt", row.Counterparty);
        Assert.Equal("CARD-1234", row.ExternalId);
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public async Task FetchAsync_MissingBalanceId_FailsActionably()
    {
        // Arrange
        var connector = new WiseConnector(
            Substitute.For<IWiseApiClient>(),
            Substitute.For<ICredentialStore>()
        );

        // Act
        var result = await connector.FetchAsync(Request(externalId: null));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("balance id", result.Error);
    }

    [Fact]
    public async Task FetchAsync_MissingToken_FailsActionably()
    {
        // Arrange
        var store = Substitute.For<ICredentialStore>();
        store
            .GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var connector = new WiseConnector(Substitute.For<IWiseApiClient>(), store);

        // Act
        var result = await connector.FetchAsync(Request());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("token", result.Error);
    }

    [Fact]
    public async Task FetchAsync_BalanceNotUnderAnyProfile_Fails()
    {
        // Arrange
        var client = Substitute.For<IWiseApiClient>();
        var store = Substitute.For<ICredentialStore>();
        store
            .GetSecretAsync(CredentialKeys.WiseApiToken(ConnectionId), Arg.Any<CancellationToken>())
            .Returns("token");
        client
            .GetProfilesAsync("token", ProviderEnvironment.Sandbox, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<WiseProfile>>([new WiseProfile(42, "personal")]));
        client
            .GetBalancesAsync(
                "token",
                ProviderEnvironment.Sandbox,
                42,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Ok<IReadOnlyList<WiseBalance>>([new WiseBalance(999, "USD", 1m)]));
        var connector = new WiseConnector(client, store);

        // Act
        var result = await connector.FetchAsync(Request());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("306149", result.Error);
    }
}
