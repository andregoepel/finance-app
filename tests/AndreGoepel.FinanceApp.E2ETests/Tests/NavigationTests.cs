namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// Every finance page is <c>[Authorize]</c>: an authenticated admin can open each one (route exists,
/// renders its heading), and an anonymous visitor is bounced to the login page.
/// </summary>
public sealed class NavigationTests(E2EAppFixture fixture) : FinanceE2ETestBase(fixture)
{
    /// <summary>Each routed page paired with the level-1 heading it renders.</summary>
    public static TheoryData<string, string> Pages =>
        new()
        {
            { "/", "Dashboard" },
            { "/transactions", "Transactions" },
            { "/review", "Review queue" },
            { "/recurring", "Recurring" },
            { "/planning", "Planning" },
            { "/import", "Import" },
            { "/sync", "Sync" },
            { "/settings/accounts", "Accounts" },
            { "/settings/categories", "Categories" },
            { "/settings/budgets", "Budgets" },
            { "/settings/rules", "Categorization rules" },
            { "/settings/connections", "Connections" },
            { "/settings/credentials", "API Keys" },
            { "/settings/crypto", "Crypto holdings" },
        };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task AuthenticatedAdmin_CanOpen_Page(string path, string heading)
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync(path);
        await Page.WaitForBlazorAsync();

        // Assert — stayed on the page (no redirect to login) and rendered its heading.
        Assert.Equal(path, new Uri(Page.Url).AbsolutePath);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = heading }).First)
            .ToBeVisibleAsync();
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task AnonymousVisitor_IsRedirectedToLogin(string path, string heading)
    {
        // Arrange — ensure first-run setup is complete (otherwise the app funnels everyone to
        // /Setup, not the login page); this provisions the admin in a throwaway context and does
        // NOT sign in this test's own context, which stays anonymous (fresh context, no cookie).
        _ = heading;
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync(path);
        await Page.WaitForBlazorAsync();

        // Assert
        await Page.AssertOnPathAsync("Account/Login");
    }
}
