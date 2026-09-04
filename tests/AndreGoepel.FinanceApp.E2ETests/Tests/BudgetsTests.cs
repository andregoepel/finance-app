namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// Budget management lives in Planning; the former settings route remains a redirect.
/// </summary>
public sealed class BudgetsTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task AddThenEditBudget_RoundTripsThroughTheDialog()
    {
        // Arrange — "Groceries" is one of the categories seeded into every fresh
        // database (DefaultCategorySeed), so no setup beyond login is needed.
        await LoginAsAdminAsync();
        await Page.GotoAsync("/settings/budgets");
        await Page.AssertOnPathAsync("planning");

        // Act — open the dialog, fill it in, save.
        await Page.ClickButtonAsync("Add budget");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        await Page.SelectDropDownAsync("Category", "Groceries");
        await Page.FillFormFieldAsync("Monthly limit (€)", "300");
        await Page.ClickDialogButtonAsync("Add");

        // Assert — the dialog closes and the grid shows the new row.
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText("Groceries").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("300.00 €").First).ToBeVisibleAsync();

        // Act — reopen it in edit mode and change the limit.
        await Page.GetByRole(AriaRole.Row, new() { Name = "Groceries" })
            .GetByLabel("Edit budget")
            .ClickAsync();
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        // Pre-filled with the existing limit, not blank — the dialog was actually
        // handed the budget being edited.
        await Expect(Page.Locator("[name='Limit']")).ToHaveValueAsync("300");
        await Page.FillFormFieldAsync("Monthly limit (€)", "450");
        await Page.ClickDialogButtonAsync("Save");

        // Assert
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText("450.00 €").First).ToBeVisibleAsync();

        // Delete remains available after the standalone Budgets page is retired.
        await Page.GetByRole(AriaRole.Row, new() { Name = "Groceries" })
            .GetByLabel("Delete")
            .ClickAsync();
        await Page.ClickDialogButtonAsync("Delete");
        await Expect(Page.GetByRole(AriaRole.Row, new() { Name = "Groceries" }))
            .Not.ToBeVisibleAsync();
    }
}
