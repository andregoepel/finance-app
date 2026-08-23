namespace AndreGoepel.FinanceApp.E2ETests.Infrastructure;

/// <summary>
/// Adds culture pinning on top of the package's <see cref="E2ETestBase{TFixture}"/>.
/// <para>
/// Every other E2E test in this suite locates elements by their visible <em>English</em> text —
/// headings, button captions, form labels. Until now that worked only because Chromium happens to
/// default to <c>en-US</c> on the CI runner, so the app's culture resolution (cookie →
/// <c>Accept-Language</c> → default) landed on English by accident rather than by instruction. A
/// runner with a different locale would have turned the whole suite red for reasons that had
/// nothing to do with the code under test.
/// </para>
/// <para>
/// Pinning here makes that explicit and costs nothing: the header is set on the context the base
/// class already created, so there is no extra navigation. Tests that want a different culture
/// call <see cref="UseCultureAsync"/> before navigating.
/// </para>
/// </summary>
public abstract class FinanceE2ETestBase(E2EAppFixture fixture)
    : E2ETestBase<E2EAppFixture>(fixture)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await UseCultureAsync("en");
    }

    /// <summary>
    /// Pins the culture for every subsequent request in this test's browser context.
    /// <para>
    /// Uses <c>Accept-Language</c> rather than the culture cookie deliberately: no cookie is set
    /// until the user picks a language, so the header is what a first-time visitor actually gets,
    /// and it avoids hard-coding the cookie's wire format here. Call before navigating — the
    /// culture is read on the request that establishes the Blazor circuit.
    /// </para>
    /// </summary>
    protected Task UseCultureAsync(string culture) =>
        Context.SetExtraHTTPHeadersAsync(
            new Dictionary<string, string> { ["Accept-Language"] = culture }
        );
}
