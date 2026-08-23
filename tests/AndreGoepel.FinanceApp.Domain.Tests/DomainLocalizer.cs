using AndreGoepel.FinanceApp.Domain.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Tests;

/// <summary>
/// The <see cref="IStringLocalizer{DomainStrings}"/> the handlers under test expect. In production
/// Wolverine method-injects it; these tests call the handlers directly, so they pass it themselves.
/// <para>
/// Deliberately the real localizer over the domain layer's embedded resources rather than a stub:
/// every assertion in these tests compares against the actual English message, so a missing or
/// misspelled resource key surfaces here as a failing test instead of quietly passing against a
/// fake. Built once — resolving it spins up a service provider, and it is immutable and used from
/// every handler test.
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
