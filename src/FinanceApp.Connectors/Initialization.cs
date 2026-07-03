using FinanceApp.Connectors.Parsing;
using FinanceApp.Connectors.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Connectors;

public static class Initialization
{
    /// <summary>
    /// Registers the statement parsers (one per provider format version) and
    /// the registry that selects them. Wise and DKB export CSV; Revolut and
    /// Easy Bank only offer XLSX. API connectors arrive with Phase 3.
    /// </summary>
    public static IServiceCollection AddConnectors(this IServiceCollection services)
    {
        services.AddSingleton<IStatementParser, WiseCsvParser>();
        services.AddSingleton<IStatementParser, RevolutXlsxParser>();
        services.AddSingleton<IStatementParser, DkbCsvParser>();
        services.AddSingleton<IStatementParser, EasyBankXlsxParser>();
        services.AddSingleton<IStatementParserRegistry, StatementParserRegistry>();

        return services;
    }
}
