using System.Text.RegularExpressions;
using AndreGoepel.FinanceApp.E2ETests.Infrastructure;

namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// The core finance flow driven end to end: create a CSV-upload account, import a real DKB statement
/// fixture through the Import UI, confirm the rows land on the Transactions grid, and re-importing the
/// same file is idempotent (every row is flagged as a duplicate, nothing new).
/// </summary>
public sealed class ImportTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly string DkbStatement = Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "dkb",
        "statement-v1.csv"
    );

    [Fact]
    public async Task ImportDkbStatement_LandsTransactions_AndIsIdempotent()
    {
        // Arrange — a fresh CSV-upload account for provider DKB (the form defaults to DKB /
        // Checking / EUR / CSV upload, so only the name has to be filled).
        await LoginAsAdminAsync();
        var accountName = $"E2E DKB {Guid.NewGuid():N}"[..16];
        await CreateCsvAccountAsync(accountName);

        // Act — upload the fixture and import the new rows.
        await Page.GotoAsync("/import");
        await SelectAccountAndUploadAsync(accountName);

        // Assert — the preview reports new rows, and the import completes.
        await Expect(Page.GetByText(NewRowsBadge).First).ToBeVisibleAsync();
        await Page.ClickButtonAsync("Import");
        await Expect(Page.GetByText("Import complete")).ToBeVisibleAsync();

        // Assert — the rows are now on the Transactions grid.
        await Page.GotoAsync("/transactions");
        await Expect(Page.Locator(".rz-data-row").First).ToBeVisibleAsync();

        // Act / Assert — re-importing the same file finds only duplicates, nothing new.
        await Page.GotoAsync("/import");
        await SelectAccountAndUploadAsync(accountName);
        await Expect(Page.GetByText("0 new", new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(Page.GetByText(DuplicatesBadge).First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Creates an account through the Accounts settings form, relying on the DKB / Checking / EUR /
    /// CSV-upload defaults. An account requires at least one owner, so the admin user is selected.
    /// </summary>
    private async Task CreateCsvAccountAsync(string name)
    {
        await Page.GotoAsync("/settings/accounts");
        await Page.FillFormFieldAsync("Name", name);
        await Page.SelectDropDownAsync("Owner", TestData.AdminEmail);
        await Page.ClickButtonAsync("Add");
        // The grid reloads with the new account once the save round-trip completes.
        await Expect(Page.GetByText(name).First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Picks the account (which enables the file control) and uploads the fixture. The file
    /// <c>&lt;InputFile&gt;</c> stays disabled until an account is selected, so wait for it to become
    /// enabled before setting the file.
    /// </summary>
    private async Task SelectAccountAndUploadAsync(string accountName)
    {
        await Page.SelectDropDownAsync("Account", accountName);
        await Expect(Page.Locator("input[type='file']")).ToBeEnabledAsync();
        await Page.UploadFileAsync(DkbStatement);
    }

    // Preview badges rendered on the Import page: "<n> new" (success) and "<n> duplicates" (warning).
    private static readonly Regex NewRowsBadge = new(@"[1-9]\d* new");
    private static readonly Regex DuplicatesBadge = new(@"[1-9]\d* duplicates");
}
