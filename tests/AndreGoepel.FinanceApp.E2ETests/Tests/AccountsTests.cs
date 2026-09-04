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
        var accountRow = Page.GetByRole(AriaRole.Row, new() { Name = name });
        await Expect(accountRow).ToBeVisibleAsync();
        await Expect(accountRow.GetByText("Dkb", new() { Exact = true })).ToBeVisibleAsync();

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

    [Fact]
    public async Task WiseAccounts_AreGroupedByConnectionWhileUnlinkedAccountsRemainVisible()
    {
        await LoginAsAdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var firstConnection = $"E2E Wise A {suffix}";
        var secondConnection = $"E2E Wise B {suffix}";

        await CreateWiseConnectionAsync(firstConnection);
        await CreateWiseConnectionAsync(secondConnection);
        await CreateWiseAccountAsync($"E2E EUR {suffix}", "EUR", firstConnection);
        await CreateWiseAccountAsync($"E2E USD {suffix}", "USD", firstConnection);
        await CreateWiseAccountAsync($"E2E GBP {suffix}", "GBP", secondConnection);
        await CreateWiseAccountAsync($"E2E JPY {suffix}", "JPY", null);

        await Page.GotoAsync("/settings/accounts");

        var firstGroup = Page.GetByText($"Wise — {firstConnection}", new() { Exact = true });
        var secondGroup = Page.GetByText($"Wise — {secondConnection}", new() { Exact = true });
        var unlinkedGroup = Page.GetByText("Wise — Unlinked accounts", new() { Exact = true });
        await Expect(firstGroup).ToBeVisibleAsync();
        await Expect(secondGroup).ToBeVisibleAsync();
        await Expect(unlinkedGroup).ToBeVisibleAsync();
        await Expect(Page.GetByText($"E2E EUR {suffix}", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText($"E2E USD {suffix}", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText($"E2E GBP {suffix}", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText($"E2E JPY {suffix}", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    private async Task CreateWiseConnectionAsync(string label)
    {
        await Page.GotoAsync("/settings/connections");
        await Page.WaitForBlazorAsync();
        await Page.FillFormFieldAsync("Label", label);
        await Page.SelectDropDownAsync("Owner", TestData.AdminEmail);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = label })).ToBeVisibleAsync();
    }

    private async Task CreateWiseAccountAsync(string name, string currency, string? connection)
    {
        await Page.GotoAsync("/settings/accounts");
        await Page.WaitForBlazorAsync();
        await Page.ClickButtonAsync("Add account");
        await Page.FillFormFieldAsync("Name", name);
        await Page.SelectDropDownAsync("Provider", "Wise");
        await Page.FillFormFieldAsync("Currency", currency);
        await Page.SelectDropDownAsync("Owner", TestData.AdminEmail);
        if (connection is not null)
        {
            await Page.SelectDropDownAsync("Sync", "Api");
            await Page.SelectDropDownAsync("Connection", connection);
        }
        await Page.ClickDialogButtonAsync("Add");
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
    }
}
