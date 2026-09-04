namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The Accounts settings page's add/edit flow moved from an always-on inline form to a
/// Radzen dialog (#166). This is the first page converted, so it is the one place that
/// actually exercises <c>DialogForm</c>/<c>DialogHeader</c> end to end against a real browser.
/// </summary>
public sealed class AccountsTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task AddThenEditAccount_RoundTripsThroughTheDialog()
    {
        // Arrange
        await LoginAsAdminAsync();
        var name = $"E2E Account {Guid.NewGuid():N}"[..18];
        await Page.GotoAsync("/settings/accounts");

        // Act — open the dialog, fill it in, save.
        await Page.ClickButtonAsync("Add account");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        await Page.FillFormFieldAsync("Name", name);
        await Page.SelectDropDownAsync("Owner", TestData.AdminEmail);
        await Page.ClickDialogButtonAsync("Add");

        // Assert — the dialog closes and the grid shows the new row.
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText(name).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Provider: Dkb").First).ToBeVisibleAsync();

        // Act — reopen it in edit mode and change the name.
        var renamed = $"{name}-edited";
        await Page.GetByRole(AriaRole.Row, new() { Name = name }).GetByLabel("Edit").ClickAsync();
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        // Pre-filled with the existing name, not blank — the dialog was actually handed the account.
        await Expect(Page.Locator("[name='Name']")).ToHaveValueAsync(name);
        await Page.FillFormFieldAsync("Name", renamed);
        await Page.ClickDialogButtonAsync("Save");

        // Assert
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText(renamed).First).ToBeVisibleAsync();
    }
}
