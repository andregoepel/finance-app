using AndreGoepel.FinanceApp.Connectors.Crypto;
using AndreGoepel.FinanceApp.Connectors.Exchange;
using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;
using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Connectors.Sync;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Exchange;
using AndreGoepel.FinanceApp.Domain.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Connectors;

public static class Initialization
{
    /// <summary>
    /// Registers the statement parsers (one per provider format version), the
    /// registry that selects them, and the API clients: Wise (balance reads +
    /// SCA-signed statement sync) and Enable Banking (PSD2 transaction sync for
    /// DKB + Revolut). Wise and DKB export CSV; Revolut and Easy Bank only offer XLSX.
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
        // No BaseAddress — the client builds absolute URLs per call because the
        // base differs by environment (Wise sandbox vs production).
        services
            .AddHttpClient(
                WiseApiClient.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(60)
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
        services
            .AddHttpClient(
                FrankfurterExchangeRateProvider.HttpClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.frankfurter.dev/v1/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                }
            )
            .RemoveAllResilienceHandlers();
        services
            .AddHttpClient(
                CoinGeckoPriceProvider.HttpClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                }
            )
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.AddScoped<IWiseApiClient, WiseApiClient>();
        services.AddScoped<IEnableBankingClient, EnableBankingClient>();
        services.AddScoped<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
        services.AddScoped<ICryptoPriceProvider, CoinGeckoPriceProvider>();

        // Transaction connectors: Enable Banking serves DKB + Revolut; Wise syncs
        // its own balance statements (SCA-signed requests). Wise balances refresh
        // separately via WiseBalanceService in the host.
        services.AddScoped<IProviderConnector, EnableBankingConnector>();
        services.AddScoped<IProviderConnector, WiseConnector>();
        services.AddScoped<IProviderConnectorRegistry, ProviderConnectorRegistry>();

        return services;
    }
}
