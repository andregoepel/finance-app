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
        // Aspire's ServiceDefaults applies a standard resilience handler to every
        // HttpClient by default; its 10s per-attempt timeout aborts Claude's slower
        // batch calls (and throws a Polly TimeoutRejectedException our catch doesn't
        // handle). This external, deliberately-slow API relies on the 90s client
        // timeout and the graceful Result-based degradation instead.
        // RemoveAllResilienceHandlers is [Experimental] (EXTEXP0001) but is the
        // intended way to opt a single client out of the global default.
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
