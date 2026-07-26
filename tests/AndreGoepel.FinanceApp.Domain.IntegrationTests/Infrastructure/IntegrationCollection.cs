namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;

/// <summary>
/// Ready-to-use xunit collection wired to the shared <see cref="MartenFixture"/> — this
/// project's document types (<c>ProviderCredential</c>) use Marten's default identity
/// conventions, so no subclass with extra store configuration is needed.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MartenFixture>
{
    public const string Name = "Integration";
}
