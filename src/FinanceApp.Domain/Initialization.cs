using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Domain;

public static class Initialization
{
    /// <summary>
    /// Registers the finance domain services. Empty in Phase 0 — the Transaction
    /// aggregate, commands and Wolverine handlers arrive with Phase 1.
    /// </summary>
    public static IServiceCollection AddFinanceDomain(this IServiceCollection services)
    {
        return services;
    }
}
