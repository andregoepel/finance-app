using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Recurring;
using Marten;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Recurring;

[Collection(IntegrationCollection.Name)]
public sealed class DismissRecurringSeriesCommandHandlerTests(FinanceMartenFixture fixture)
    : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Handle_StoresADismissedSeriesByCounterparty()
    {
        // Act
        await DismissAsync("Netflix");

        // Assert
        await using var session = fixture.Store.QuerySession();
        var dismissed = await session.LoadAsync<DismissedRecurringSeries>("Netflix", Ct);
        Assert.NotNull(dismissed);
    }

    [Fact]
    public async Task Handle_DismissingTwice_IsIdempotent()
    {
        // Act
        await DismissAsync("Netflix");
        await DismissAsync("Netflix");

        // Assert — one document, not two, and no error on the second call.
        await using var session = fixture.Store.QuerySession();
        var all = await session.Query<DismissedRecurringSeries>().ToListAsync(Ct);
        Assert.Single(all);
    }

    private async Task DismissAsync(string counterparty)
    {
        await using var session = fixture.Store.LightweightSession();
        await DismissRecurringSeriesCommandHandler.Handle(
            new DismissRecurringSeriesCommand(counterparty),
            session,
            Ct
        );
    }
}
