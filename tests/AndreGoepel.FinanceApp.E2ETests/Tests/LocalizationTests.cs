namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The German counterpart to the rest of the suite, which drives everything by visible English
/// text. Everything else — the resx parity test, the per-key pinning tests, the bUnit German render
/// test — verifies pieces in isolation or against an in-process localizer. This is the only place
/// the whole chain is exercised as a user meets it: a real browser sending
/// <c>Accept-Language: de</c>, the request-localization middleware resolving it, the Blazor circuit
/// inheriting that culture, and the embedded <c>.de.resx</c> feeding the rendered page.
/// <para>
/// The German tests switch culture <em>before</em> logging in, so the login page is itself rendered
/// in German — the identity package's account pages are part of the chain a German-speaking user
/// walks through, and driving them proves the culture survives the sign-in redirect rather than
/// being established afterwards.
/// </para>
/// </summary>
public sealed class LocalizationTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task GermanCulture_Dashboard_RendersGermanChromeAndHeading()
    {
        // Arrange — switch culture first, so the login page is driven in German too.
        await UseCultureAsync("de");
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert — the page's own heading and the nav chrome both come from the app's resx.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Übersicht" }).First)
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Einnahmen").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Transaktionen").First).ToBeVisibleAsync();

        // Assert — and the English is genuinely gone, not merely joined by German. Without this a
        // silent fallback to the neutral resx would still pass every assertion above.
        Assert.DoesNotContain("Spending by category", await Page.ContentAsync());
    }

    [Fact]
    public async Task GermanCulture_SettingsPage_RendersGermanBreadcrumbAndEnumLabels()
    {
        // Arrange — switch culture first, so the login page is driven in German too.
        await UseCultureAsync("de");
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/settings/accounts");
        await Page.WaitForBlazorAsync();

        // Assert — the breadcrumb composes a localized frame with a localized page name.
        var content = await Page.ContentAsync();
        Assert.Contains("Einstellungen / Konten", content);
        Assert.DoesNotContain("Settings / Accounts", content);

        // Act — the account-type dropdown only renders inside the add/edit dialog (#166 moved it
        // out of the always-visible inline form).
        await Page.ClickButtonAsync("Konto hinzufügen");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();

        // Assert — the account-type dropdown renders enum members through the resx rather than as
        // C# identifiers. (Radzen derives a dropdown's aria-label from the bound value rather than
        // from its Template, so the C# enum name ("Checking") legitimately survives in the HTML
        // even though every visible occurrence is German — a whole-document negative check would
        // fail for a reason unrelated to localization, so there isn't one here.)
        Assert.Contains("Girokonto", await Page.ContentAsync());
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
