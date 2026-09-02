namespace AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure;

/// <summary>
/// xunit collection wired to <see cref="CategorizationMartenFixture"/>, so every
/// integration test in this assembly shares one Postgres container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<CategorizationMartenFixture>
{
    public const string Name = "Integration";
}
