using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;
using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Connectors.Sync;
using AndreGoepel.FinanceApp.Domain.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Connectors;

public static class Initialization
{
    /// <summary>
    /// Registers the statement parsers (one per provider format version), the
    /// registry that selects them, and the Phase 3 API connectors: Wise (personal
    /// API + SCA signing) and Enable Banking (PSD2 aggregator for DKB + Revolut).
    /// Wise and DKB export CSV; Revolut and Easy Bank only offer XLSX.
    /// </summary>
    public static IServiceCollection AddConnectors(this IServiceCollection services)
    {
        services.AddSingleton<IStatementParser, WiseCsvParser>();
        services.AddSingleton<IStatementParser, RevolutXlsxParser>();
        services.AddSingleton<IStatementParser, DkbCsvParser>();
        services.AddSingleton<IStatementParser, EasyBankXlsxParser>();
        services.AddSingleton<IStatementParserRegistry, StatementParserRegistry>();

        // External bank APIs are deliberately slow; like the Claude client they opt
        // out of Aspire's default per-attempt resilience timeout and rely on a
        // generous client timeout plus Result-based graceful degradation.
#pragma warning disable EXTEXP0001
        services
            .AddHttpClient(
                WiseApiClient.HttpClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.transferwise.com/");
                    client.Timeout = TimeSpan.FromSeconds(60);
                }
            )
            .RemoveAllResilienceHandlers();
        services
            .AddHttpClient(
                EnableBankingClient.HttpClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.enablebanking.com/");
                    client.Timeout = TimeSpan.FromSeconds(60);
                }
            )
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.AddScoped<IWiseApiClient, WiseApiClient>();
        services.AddScoped<IEnableBankingClient, EnableBankingClient>();

        services.AddScoped<IProviderConnector, WiseConnector>();
        services.AddScoped<IProviderConnector, EnableBankingConnector>();
        services.AddScoped<IProviderConnectorRegistry, ProviderConnectorRegistry>();

        return services;
    }
}
