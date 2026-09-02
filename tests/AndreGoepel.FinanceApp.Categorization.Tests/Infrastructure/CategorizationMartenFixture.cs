using Marten;

namespace AndreGoepel.FinanceApp.Categorization.Tests.Infrastructure;

/// <summary>
/// <see cref="MartenFixture"/> configured like the running app by reusing
/// <see cref="Domain.Initialization.ConfigureStore"/> — the inline
/// <c>TransactionView</c> snapshot included — so the store the categorization
/// handler is tested against can never drift from production.
/// </summary>
public sealed class CategorizationMartenFixture : MartenFixture
{
    protected override void ConfigureStore(StoreOptions options) =>
        Domain.Initialization.ConfigureStore(options);

    public override async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await base.ResetAsync(cancellationToken);
        await Store.Advanced.Clean.DeleteAllEventDataAsync(cancellationToken);
    }
}
