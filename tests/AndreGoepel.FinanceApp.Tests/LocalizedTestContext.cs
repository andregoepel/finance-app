using AndreGoepel.Testing.Bunit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace AndreGoepel.FinanceApp.Tests;

/// <summary>
/// Base bUnit context for component tests that render localized strings. Registers the
/// localization services — so <c>IStringLocalizer&lt;Strings&gt;</c> resolves against the app's
/// embedded <c>.resx</c> — and the Radzen ambient services, and offers a scoped culture switch so
/// a test can pin the UI culture it renders under.
/// </summary>
public abstract class LocalizedTestContext : BunitContext
{
    protected LocalizedTestContext()
    {
        // RadzenDataGrid and friends reach for JS and the Radzen ambient services; loosen JS
        // and register localization, then add the Radzen ambient services (out of scope for
        // AndreGoepel.Testing.Bunit — see its README).
        this.UseLooseJSInterop().UseLocalization();
        Services.AddRadzenComponents();
    }

    /// <summary>
    /// Pins the current UI culture (and format culture) for the duration of the returned scope,
    /// restoring the previous cultures on dispose. <c>IStringLocalizer</c> reads
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> at lookup time, so wrapping
    /// the render call chooses the language a component renders in.
    /// </summary>
    /// <param name="culture">The culture name, e.g. <c>"en"</c> or <c>"de"</c>.</param>
    protected static IDisposable UseCulture(string culture) => new CultureScope(culture);
}
