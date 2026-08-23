using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Connectors.Sync;
using AndreGoepel.FinanceApp.Domain.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Sync;

public sealed class ProviderConnectorRegistryTests
{
    private readonly EnableBankingConnector enableBanking = new(
        Substitute.For<IEnableBankingClient>(),
        DomainLocalizer.Instance,
        NullLogger<EnableBankingConnector>.Instance
    );

    private ProviderConnectorRegistry BuildRegistry() => new([enableBanking]);

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

    [Theory]
    [InlineData(ProviderKind.EasyBank)] // CSV/XLSX only
    [InlineData(ProviderKind.Wise)] // token-only balance reader, not a transaction connector
    public void ForProvider_NonTransactionProvider_FailsLoudly(ProviderKind provider)
    {
        // Act
        var result = BuildRegistry().ForProvider(provider);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("No API connector", result.Error);
    }
}
