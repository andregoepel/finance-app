using AndreGoepel.FinanceApp.E2ETests.Infrastructure;

namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>
/// Every finance page is <c>[Authorize]</c>: an authenticated admin can open each one (route exists,
/// renders its heading), and an anonymous visitor is bounced to the login page.
/// </summary>
public sealed class NavigationTests(E2EAppFixture fixture) : E2ETestBase(fixture)
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
        // Arrange — a fresh context per test means no session cookie.
        _ = heading;

        // Act
        await Page.GotoAsync(path);
        await Page.WaitForBlazorAsync();

        // Assert
        await Page.AssertOnPathAsync("Account/Login");
    }
}
