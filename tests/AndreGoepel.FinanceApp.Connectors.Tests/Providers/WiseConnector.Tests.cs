using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public sealed class WiseConnectorTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static ProviderSyncRequest Request(
        string? externalId = "306149",
        string? currency = "EUR"
    ) =>
        new(
            AccountId: Guid.NewGuid(),
            Provider: ProviderKind.Wise,
            ConnectionId: ConnectionId,
            ExternalId: externalId,
            IdentificationHash: null,
            ProviderAccountReference: null,
            Since: new DateOnly(2026, 6, 1),
            Environment: ProviderEnvironment.Sandbox,
            Currency: currency
        );

    private static WiseActivity Activity(
        string id,
        decimal amount,
        string currency = "EUR",
        string status = "COMPLETED"
    ) =>
        new(
            Id: id,
            Type: "TRANSFER",
            Status: status,
            Date: new DateOnly(2026, 7, 4),
            Amount: amount,
            Currency: currency,
            Title: "André Example",
            Description: "Sending",
            RawJson: "{}"
        );

    [Fact]
    public void Supports_ProviderKind_ReturnsTrueOnlyForWise()
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
    public async Task FetchAsync_HappyPath_KeepsOnlyCompletedRowsInAccountCurrency()
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
            .Returns(Result.Ok<IReadOnlyList<WiseBalance>>([new WiseBalance(306149, "EUR", 100m)]));
        client
            .GetActivitiesAsync(
                "token",
                ProviderEnvironment.Sandbox,
                42,
                new DateOnly(2026, 6, 1),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Ok<IReadOnlyList<WiseActivity>>([
                    Activity("keep", -666m),
                    Activity("wrong-currency", -10m, currency: "USD"),
                    Activity("in-progress", -20m, status: "IN_PROGRESS"),
                ])
            );
        var connector = new WiseConnector(client, store);

        // Act
        var result = await connector.FetchAsync(Request());

        // Assert — USD and IN_PROGRESS rows stay out; the EUR completed one imports.
        Assert.True(result.IsSuccess);
        Assert.Equal("wise-api-v1", result.Value!.SyncSource);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(-666m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal("André Example", row.Counterparty);
        Assert.Equal("keep", row.ExternalId);
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public void MapForCurrency_Conversion_BooksBothSidesWithCorrectSigns()
    {
        // Arrange — the live sandbox conversion: 11,002 EUR spent → 9,329.84 GBP received.
        var conversion = new WiseActivity(
            Id: "conversion-id",
            Type: "INTERBALANCE",
            Status: "COMPLETED",
            Date: new DateOnly(2026, 7, 13),
            Amount: -9329.84m, // primary parses as money out; the mapper corrects per side
            Currency: "GBP",
            Title: "To GBP",
            Description: "Moved",
            RawJson: "{}",
            SecondaryAmount: 11002m,
            SecondaryCurrency: "EUR"
        );

        // Act
        var eurSide = WiseConnector.MapForCurrency(conversion, "EUR");
        var gbpSide = WiseConnector.MapForCurrency(conversion, "GBP");
        var usdSide = WiseConnector.MapForCurrency(conversion, "USD");

        // Assert — source account books money out, target account money in.
        Assert.Equal(-11002m, eurSide!.Amount);
        Assert.Equal("EUR", eurSide.Currency);
        Assert.Equal(9329.84m, gbpSide!.Amount);
        Assert.Equal("GBP", gbpSide.Currency);
        Assert.Null(usdSide);
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
