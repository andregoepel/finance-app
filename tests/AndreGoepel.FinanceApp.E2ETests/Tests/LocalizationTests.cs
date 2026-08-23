namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The German counterpart to the rest of the suite, which drives everything by visible English
/// text. Everything else — the resx parity test, the per-key pinning tests, the bUnit German render
/// test — verifies pieces in isolation or against an in-process localizer. This is the only place
/// the whole chain is exercised as a user meets it: a real browser sending
/// <c>Accept-Language: de</c>, the request-localization middleware resolving it, the Blazor circuit
/// inheriting that culture, and the embedded <c>.de.resx</c> feeding the rendered page.
/// <para>
/// Each test logs in <em>before</em> switching culture. That is a workaround, not a preference:
/// <c>AndreGoepel.Testing.E2E</c>'s account helpers locate the submit control by its English
/// caption (<c>ClickButtonAsync("Log in")</c>), so they cannot drive the identity package's
/// German-rendered login page — verified by a real run, where both German tests timed out waiting
/// for that button. The auth cookie survives the switch, and what these tests assert is the
/// finance app's own pages rather than the identity package's, so the coverage is unaffected.
/// Teaching the shared package to log in under any culture is worth its own issue.
/// </para>
/// </summary>
public sealed class LocalizationTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task GermanCulture_Dashboard_RendersGermanChromeAndHeading()
    {
        // Arrange — log in first (see the note on this class), then switch culture.
        await LoginAsAdminAsync();
        await UseCultureAsync("de");

        // Act
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert — the page's own heading and the nav chrome both come from the app's resx.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Übersicht" }).First)
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Einnahmen").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Umsätze").First).ToBeVisibleAsync();

        // Assert — and the English is genuinely gone, not merely joined by German. Without this a
        // silent fallback to the neutral resx would still pass every assertion above.
        Assert.DoesNotContain("Spending by category", await Page.ContentAsync());
    }

    [Fact]
    public async Task GermanCulture_SettingsPage_RendersGermanBreadcrumbAndEnumLabels()
    {
        // Arrange — log in first (see the note on this class), then switch culture.
        await LoginAsAdminAsync();
        await UseCultureAsync("de");

        // Act
        await Page.GotoAsync("/settings/accounts");
        await Page.WaitForBlazorAsync();

        // Assert — the breadcrumb composes a localized frame with a localized page name, and the
        // account-type dropdown renders enum members through the resx rather than as C# identifiers.
        var content = await Page.ContentAsync();
        Assert.Contains("Einstellungen / Konten", content);
        Assert.Contains("Girokonto", content);

        // The negative check is on prose, not on the raw markup. Radzen derives a dropdown's
        // aria-label from the bound value rather than from its Template, so the C# enum name
        // ("Checking") legitimately survives in the HTML even though every visible occurrence is
        // German. Asserting against the whole document would fail for a reason unrelated to
        // localization — worth knowing, and worth a separate look at the accessible name.
        Assert.DoesNotContain("Settings / Accounts", content);
    }

    [Fact]
    public async Task EnglishCulture_IsWhatTheRestOfTheSuiteGets()
    {
        // Arrange / Act — the base class pins "en"; this asserts that pin actually takes effect,
        // so the English-text locators every other test relies on cannot silently start resolving
        // against a different culture.
        await LoginAsAdminAsync();
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).First)
            .ToBeVisibleAsync();
        Assert.DoesNotContain("Ausgaben nach Kategorie", await Page.ContentAsync());
    }
}
