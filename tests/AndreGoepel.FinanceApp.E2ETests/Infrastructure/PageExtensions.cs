namespace AndreGoepel.Testing.E2E;

/// <summary>
/// App-specific page helpers layered on top of <see cref="AndreGoepel.Testing.E2E.PageExtensions"/>'s
/// verbatim-identical core: Radzen grid/dropdown selectors, design-system <c>FormField</c> locators, and
/// file upload — deliberately kept local rather than forked into the shared package (see that package's
/// <c>PageExtensions</c> XML docs).
/// </summary>
public static class FinanceAppPageExtensions
{
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

    /// <summary>
    /// Clicks a button by its visible text, scoped to the open Radzen dialog
    /// (<c>.rz-dialog-content</c>). A trigger button that opened the dialog (e.g.
    /// "Add account") stays in the DOM behind it, so an unscoped
    /// <see cref="ClickButtonAsync"/> risks matching the wrong one whenever the
    /// dialog's own button caption is a substring of the trigger's (e.g. "Add").
    /// </summary>
    public static Task ClickDialogButtonAsync(this IPage page, string text) =>
        page.Locator(".rz-dialog-content")
            .GetByRole(AriaRole.Button, new() { Name = text, Exact = false })
            .First.ClickAsync();
}
