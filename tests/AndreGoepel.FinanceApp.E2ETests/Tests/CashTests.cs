namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The Cash page's two inline forms (create account, record a manual entry)
/// both moved into Radzen dialogs (#166, page 4 of 4 — the last one).
/// </summary>
public sealed class CashTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    [Fact]
    public async Task CreateAccountThenRecordEntry_BothGoThroughTheirOwnDialog()
    {
        // Arrange — a fresh admin has no cash account yet, so the page renders
        // its EmptyState with the "Create cash account" trigger.
        await LoginAsAdminAsync();
        await Page.GotoAsync("/cash");

        // Act — create the account via its dialog.
        await Page.ClickButtonAsync("Create cash account");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        var accountName = $"E2E Wallet {Guid.NewGuid():N}"[..16];
        await Page.FillFormFieldAsync("Name", accountName);
        await Page.ClickDialogButtonAsync("Create");

        // Assert — the dialog closes and the page now shows the account's balance card.
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText("Cash on hand —").First).ToBeVisibleAsync();

        // Act — record a manual entry via its own dialog.
        await Page.ClickButtonAsync("Record transaction");
        await Expect(Page.Locator(".rz-dialog-content")).ToBeVisibleAsync();
        var description = $"E2E groceries {Guid.NewGuid():N}"[..20];
        await Page.FillFormFieldAsync("Amount (EUR)", "42.50");
        await Page.FillFormFieldAsync("Description", description);
        await Page.ClickDialogButtonAsync("Record");

        // Assert — the dialog closes and the entry shows up in the transactions grid.
        await Expect(Page.Locator(".rz-dialog-content")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText(description).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("-42.50 €").First).ToBeVisibleAsync();
    }
}
