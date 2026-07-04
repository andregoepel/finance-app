using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Connectors.Sync;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Sync;

public class ProviderConnectorRegistryTests
{
    private readonly WiseConnector wise = new(
        Substitute.For<IWiseApiClient>(),
        Substitute.For<ICredentialStore>()
    );
    private readonly EnableBankingConnector enableBanking = new(
        Substitute.For<IEnableBankingClient>()
    );

    private ProviderConnectorRegistry BuildRegistry() => new([wise, enableBanking]);

    [Fact]
    public void ForProvider_Wise_ReturnsWiseConnector()
    {
        // Act
        var result = BuildRegistry().ForProvider(ProviderKind.Wise);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(wise, result.Value);
    }

    [Theory]
    [InlineData(ProviderKind.Dkb)]
    [InlineData(ProviderKind.Revolut)]
    public void ForProvider_EnableBankingProviders_ReturnTheAggregatorConnector(
        ProviderKind provider
    )
    {
        // Act
        var result = BuildRegistry().ForProvider(provider);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(enableBanking, result.Value);
    }

    [Fact]
    public void ForProvider_ImportOnlyProvider_FailsLoudly()
    {
        // Act — Easy Bank is CSV/XLSX only, no API connector.
        var result = BuildRegistry().ForProvider(ProviderKind.EasyBank);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("import-only", result.Error);
    }
}
