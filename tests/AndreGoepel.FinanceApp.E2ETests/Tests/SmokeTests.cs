namespace AndreGoepel.FinanceApp.E2ETests.Tests;

/// <summary>Fast confidence checks that the harness boots the app and the core happy path works.</summary>
public sealed class SmokeTests(E2EAppFixture fixture) : E2ETestBase<E2EAppFixture>(fixture)
{
    [Fact]
    public async Task Setup_ProvisionsAdmin_AndSetupPageIsShownOnlyOnce()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/Setup");
        await Page.WaitForBlazorAsync();

        // Assert — once an admin exists, Setup redirects away from itself.
        Assert.DoesNotContain(
            "/Setup",
            new Uri(Page.Url).AbsolutePath,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task Admin_CanLogIn_AndReachDashboard()
    {
        // Arrange / Act
        await LoginAsAdminAsync();
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert — the dashboard is the authenticated landing page at the app root.
        Assert.Equal("/", new Uri(Page.Url).AbsolutePath);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).First)
            .ToBeVisibleAsync();
    }
}
