using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Connectors;

public static class Initialization
{
    /// <summary>
    /// Registers provider connectors and statement parsers. Empty in Phase 0 —
    /// CSV parsers arrive with Phase 1, API connectors with Phase 3.
    /// </summary>
    public static IServiceCollection AddConnectors(this IServiceCollection services)
    {
        return services;
    }
}
