using AndreGoepel.Testing.E2E;
using Aspire.Hosting.Testing;

namespace AndreGoepel.FinanceApp.E2ETests.Infrastructure;

#region Fixture

/// <summary>
/// Configures the shared <see cref="AndreGoepel.Testing.E2E.E2EAppFixture"/> for this app: boots the
/// AppHost (Postgres + MailHog + the Blazor web app) with <c>E2E=true</c> so Postgres runs without its
/// persistent volume, and the database password secret parameter this AppHost requires with no default.
/// </summary>
public sealed class E2EAppFixture()
    : AndreGoepel.Testing.E2E.E2EAppFixture(
        new E2EAppFixtureOptions
        {
            CreateAppHostBuilder = args =>
                DistributedApplicationTestingBuilder.CreateAsync<Projects.AndreGoepel_FinanceApp_AppHost>(
                    args
                ),
            WebResourceName = "financeapp",
            // The AppFoundation Setup page's submit button reads "Create admin & complete setup";
            // matched on a stable prefix.
            ProvisionAdminButtonText = "Create admin",
            MailHogResourceName = "mailhog",
            // This AppHost still names MailHog's HTTP endpoint "web" (see AppHost.cs) rather than the
            // package's canonical "http" default — tracked separately in #69.
            MailHogEndpointName = "web",
            AppHostArguments = ["E2E=true", "Parameters:database-password=E2e-Db-Passw0rd!"],
        }
    );

#endregion

#region Collection

[CollectionDefinition(E2ECollectionDefaults.Name)]
public sealed class E2ECollection : ICollectionFixture<E2EAppFixture>;

#endregion
