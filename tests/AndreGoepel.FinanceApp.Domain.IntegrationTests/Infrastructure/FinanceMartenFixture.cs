using Marten;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="MartenFixture"/> configured exactly like the running app — the
/// inline <c>TransactionView</c> snapshot included — by reusing
/// <see cref="Initialization.ConfigureStore"/> rather than re-declaring the
/// projections here, so the test store can never drift from production.
/// </summary>
public sealed class FinanceMartenFixture : MartenFixture
{
    protected override void ConfigureStore(StoreOptions options) =>
        Initialization.ConfigureStore(options);

    public override async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await base.ResetAsync(cancellationToken);
        await Store.Advanced.Clean.DeleteAllEventDataAsync(cancellationToken);
    }
}
