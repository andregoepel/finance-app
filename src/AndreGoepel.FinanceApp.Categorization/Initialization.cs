using AndreGoepel.FinanceApp.Categorization.Claude;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Categorization;

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
        // A named client (not a typed client) plus a plain service registration, so
        // Wolverine can inline-construct the handler's IClaudeCategorizer dependency
        // (typed HttpClient registrations require forbidden service location).
        //
        // RemoveAllResilienceHandlers: Claude's 90s timeout needs to bypass Aspire's 10s default (EXTEXP0001).
#pragma warning disable EXTEXP0001
        services
            .AddHttpClient(
                ClaudeCategorizer.HttpClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.anthropic.com/");
                    client.Timeout = TimeSpan.FromSeconds(90);
                }
            )
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddScoped<IClaudeCategorizer, ClaudeCategorizer>();

        return services;
    }
}
