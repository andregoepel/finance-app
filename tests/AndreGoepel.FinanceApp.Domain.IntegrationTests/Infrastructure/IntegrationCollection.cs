namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;

/// <summary>
/// Ready-to-use xunit collection wired to <see cref="FinanceMartenFixture"/> — the
/// shared <c>MartenFixture</c> plus this domain's store configuration (the inline
/// <c>TransactionView</c> projection), so every integration test in this assembly
/// shares one container.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<FinanceMartenFixture>
{
    public const string Name = "Integration";
}
