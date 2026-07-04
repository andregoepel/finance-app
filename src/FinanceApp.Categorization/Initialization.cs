using FinanceApp.Categorization.Claude;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Categorization;

public static class Initialization
{
    /// <summary>
    /// Registers the categorization services: the Claude API client (key from the
    /// encrypted credential store). The handler assembly is opted into Wolverine
    /// discovery from <c>Program.cs</c> via
    /// <c>AppFoundationOptions.ConfigureWolverine</c>. The rules engine is pure logic.
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
