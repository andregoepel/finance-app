using AndreGoepel.FinanceApp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Tests;

/// <summary>
/// Builds a real <see cref="IStringLocalizer{Strings}"/> against the app's embedded resources,
/// for the tests of code that composes localized strings outside a component.
/// </summary>
internal static class FinanceLocalizer
{
    public static IStringLocalizer<Strings> Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        return services.BuildServiceProvider().GetRequiredService<IStringLocalizer<Strings>>();
    }
}
