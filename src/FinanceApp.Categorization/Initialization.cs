using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Categorization;

public static class Initialization
{
    /// <summary>
    /// Registers the categorization services. Empty in Phase 0 — the rules engine
    /// and the Claude API client arrive with Phase 2.
    /// </summary>
    public static IServiceCollection AddCategorization(this IServiceCollection services)
    {
        return services;
    }
}
