using AndreGoepel.FinanceApp.Domain.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Connectors.Tests;

/// <summary>
/// The <see cref="IStringLocalizer{DomainStrings}"/> the connectors under test expect. In
/// production DI supplies it; these tests construct the connectors directly, so they supply it
/// themselves.
/// <para>
/// Deliberately the real localizer over the domain layer's embedded resources rather than a stub:
/// the assertions here compare against the actual English message, so a missing or misspelled
/// resource key fails a test instead of quietly passing against a fake. Built once — resolving it
/// spins up a service provider, and it is immutable.
/// </para>
/// </summary>
internal static class DomainLocalizer
{
    public static IStringLocalizer<DomainStrings> Instance { get; } = Build();

    private static IStringLocalizer<DomainStrings> Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        return services
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<DomainStrings>>();
    }
}
