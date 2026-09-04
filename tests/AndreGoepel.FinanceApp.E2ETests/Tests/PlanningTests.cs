namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The Planning page's planned-item add/edit form moved from an always-visible inline
/// section to a Radzen dialog (#166, page 3 of 4).
/// </summary>
public sealed class PlanningTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task AddThenEditPlannedItem_RoundTripsThroughTheDialog()
    {
        // Arrange
        await LoginAsAdminAsync();
        var description = $"E2E Rent {Guid.NewGuid():N}"[..14];
        await Page.GotoAsync("/planning");

        // Act — open the dialog, fill it in, save.
        await Page.ClickButtonAsync("Add planned item");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        await Page.FillFormFieldAsync("Description", description);
        await Page.FillFormFieldAsync("Amount (€)", "500");
        await Page.ClickDialogButtonAsync("Add");

        // Assert — the dialog closes and the grid shows the new row.
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText(description).First).ToBeVisibleAsync();

        // Act — reopen it in edit mode and change the amount.
        await Page.GetByRole(AriaRole.Row, new() { Name = description })
            .GetByLabel("Edit")
            .ClickAsync();
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        // Pre-filled with the existing description, not blank — the dialog was
        // actually handed the item being edited.
        await Expect(Page.Locator("[name='Description']")).ToHaveValueAsync(description);
        await Expect(Page.Locator("[name='Amount']")).ToHaveValueAsync("500");
        await Page.FillFormFieldAsync("Amount (€)", "650");
        await Page.ClickDialogButtonAsync("Save");

        // Assert — an expense renders with its sign (AmountText / Money.Format).
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText(description).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("-650.00 €").First).ToBeVisibleAsync();
    }
}
