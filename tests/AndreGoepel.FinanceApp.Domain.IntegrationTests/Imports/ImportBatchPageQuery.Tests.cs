using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure;
using AndreGoepel.FinanceApp.Domain.Providers;
using IntegrationCollection = AndreGoepel.FinanceApp.Domain.IntegrationTests.Infrastructure.IntegrationCollection;

namespace AndreGoepel.FinanceApp.Domain.IntegrationTests.Imports;

[Collection(IntegrationCollection.Name)]
public sealed class ImportBatchPageQueryTests(FinanceMartenFixture fixture) : IAsyncLifetime
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LoadAsync_ReturnsOlderPagesAndCompleteAccountCountNewestFirst()
    {
        var accountId = Guid.NewGuid();
        var batches = Enumerable.Range(1, 35).Select(index => Create(index, accountId)).ToList();
        await StoreAsync([.. batches, Create(1, Guid.NewGuid())]);

        await using var session = fixture.Store.QuerySession();
        var page = await ImportBatchPageQuery.LoadAsync(session, accountId, 20, 10, Ct);

        Assert.Equal(35, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(
            batches
                .OrderByDescending(batch => batch.ImportedAt)
                .ThenByDescending(batch => batch.Id)
                .Skip(20)
                .Take(10)
                .Select(batch => batch.Id),
            page.Items.Select(batch => batch.Id)
        );
    }

    [Fact]
    public async Task LoadAsync_UsesIdAsTieBreakerForDeterministicOrdering()
    {
        var accountId = Guid.NewGuid();
        var importedAt = DateTimeOffset.UtcNow;
        var first = Create(1, accountId, importedAt);
        var second = Create(2, accountId, importedAt);
        await StoreAsync([first, second]);

        await using var session = fixture.Store.QuerySession();
        var page = await ImportBatchPageQuery.LoadAsync(session, accountId, 0, 10, Ct);

        Assert.Equal(
            new[] { first, second }.OrderByDescending(batch => batch.Id).Select(batch => batch.Id),
            page.Items.Select(batch => batch.Id)
        );
    }

    private async Task StoreAsync(IReadOnlyList<ImportBatch> batches)
    {
        await using var session = fixture.Store.LightweightSession();
        foreach (var batch in batches)
        {
            session.Store(batch);
        }
        await session.SaveChangesAsync(Ct);
    }

    private static ImportBatch Create(
        int index,
        Guid accountId,
        DateTimeOffset? importedAt = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Provider = ProviderKind.Dkb,
            Source = $"statement-{index}.csv",
            ParserId = "dkb-csv-v1",
            ImportedAt =
                importedAt
                ?? new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-index),
            TotalRows = 1,
            ImportedCount = 1,
        };
}
