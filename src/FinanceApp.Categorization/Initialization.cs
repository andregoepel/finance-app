using FinanceApp.Categorization.Claude;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Attributes;

[assembly: WolverineModule]

namespace FinanceApp.Categorization;

public static class Initialization
{
    /// <summary>
    /// Registers the categorization services: the Claude API client (key from
    /// the encrypted credential store) and, via Wolverine module discovery, the
    /// async categorization pipeline. The rules engine is pure logic.
    /// </summary>
    public static IServiceCollection AddCategorization(this IServiceCollection services)
    {
        services.AddHttpClient<IClaudeCategorizer, ClaudeCategorizer>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        return services;
    }
}
