using Marten;

namespace AndreGoepel.FinanceApp.Domain.Imports;

public sealed record ImportBatchPage(int TotalCount, IReadOnlyList<ImportBatch> Items);

public static class ImportBatchPageQuery
{
    public static async Task<ImportBatchPage> LoadAsync(
        IQuerySession session,
        Guid accountId,
        int skip,
        int take,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        var query = session.Query<ImportBatch>().Where(batch => batch.AccountId == accountId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(batch => batch.ImportedAt)
            .ThenByDescending(batch => batch.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new ImportBatchPage(totalCount, items);
    }
}
