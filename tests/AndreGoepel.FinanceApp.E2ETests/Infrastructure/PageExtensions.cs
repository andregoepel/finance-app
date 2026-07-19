namespace AndreGoepel.FinanceApp.E2ETests.Infrastructure;

/// <summary>
/// Helpers that hide the Blazor-Server timing and Radzen markup details from individual tests, so a
/// test reads as intent ("fill Email, click Log in") rather than selector plumbing.
/// </summary>
public static class PageExtensions
{
    /// <summary>
    /// Waits until the interactive Server circuit is live. Radzen forms submit through Blazor event
    /// handlers, so clicking before the circuit connects silently does nothing — this prevents flakes.
    /// </summary>
    public static async Task WaitForBlazorAsync(this IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForFunctionAsync(
            "() => window.Blazor !== undefined && !document.querySelector('#components-reconnect-modal.components-reconnect-show')"
        );
    }

    /// <summary>Navigates to a relative path (resolved against the fixture base URL) and waits for interactivity.</summary>
    public static async Task GotoAsync(this IPage page, string path)
    {
        await page.GotoAsync(path);
        await page.WaitForBlazorAsync();
    }

    /// <summary>Fills an input rendered with <c>name="..."</c> (the identity account forms).</summary>
    public static Task FillFieldAsync(this IPage page, string name, string value) =>
        page.FillAsync($"[name='{name}']", value);

    /// <summary>Clicks a button by its visible text.</summary>
    public static Task ClickButtonAsync(this IPage page, string text) =>
        page.GetByRole(AriaRole.Button, new() { Name = text, Exact = false }).First.ClickAsync();

    /// <summary>Clicks a link by its visible text.</summary>
    public static Task ClickLinkAsync(this IPage page, string text) =>
        page.GetByRole(AriaRole.Link, new() { Name = text, Exact = false }).First.ClickAsync();

    /// <summary>
    /// Fills the text input of the design-system <c>FormField</c> whose label matches
    /// <paramref name="label"/>. <c>FormField</c> renders the label above the control (no
    /// <c>.rz-form-field</c> wrapper), so the field is located from its <c>&lt;label&gt;</c> — the
    /// input sits inside the same label-above stack (the label's parent).
    /// </summary>
    public static Task FillFormFieldAsync(this IPage page, string label, string value) =>
        page.Locator($"xpath=//label[normalize-space(.)='{label}']/..//input")
            .First.FillAsync(value);

    /// <summary>
    /// Selects an option from the <c>RadzenDropDown</c> of the <c>FormField</c> whose label matches
    /// <paramref name="label"/>: opens the panel from the field's trigger, then clicks the item
    /// whose text is <paramref name="optionText"/>.
    /// </summary>
    public static async Task SelectDropDownAsync(this IPage page, string label, string optionText)
    {
        await page.Locator(
                $"xpath=//label[normalize-space(.)='{label}']/..//div[contains(@class,'rz-dropdown')]"
            )
            .First.ClickAsync();
        await page.Locator($".rz-dropdown-panel .rz-dropdown-item:has-text('{optionText}')")
            .First.ClickAsync();
    }

    /// <summary>Sets the file on the first <c>&lt;InputFile&gt;</c> control on the page.</summary>
    public static Task UploadFileAsync(this IPage page, string absolutePath) =>
        page.SetInputFilesAsync("input[type='file']", absolutePath);

    /// <summary>Asserts the current URL path matches (ignoring query string and trailing slash).</summary>
    public static async Task AssertOnPathAsync(this IPage page, string expectedPath)
    {
        try
        {
            await page.WaitForURLAsync(
                url =>
                    NormalizePath(url)
                        .Contains(expectedPath.Trim('/'), StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 15_000 }
            );
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"Expected path to contain '{expectedPath}' but was '{page.Url}'."
            );
        }
    }

    private static string NormalizePath(string url) => new Uri(url).AbsolutePath.Trim('/');
}
