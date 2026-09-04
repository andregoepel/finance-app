using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

public sealed record ReviewQueueFilters(
    Guid? AccountId,
    DateOnly? From,
    DateOnly? To,
    bool? Income,
    string? SearchText
);

public sealed record ReviewQueuePage(int TotalCount, IReadOnlyList<TransactionView> Items);

public static class ReviewQueueQuery
{
    public static async Task<ReviewQueuePage> LoadAsync(
        IQuerySession session,
        ReviewQueueFilters filters,
        int skip,
        int take,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        var query = session
            .Query<TransactionView>()
            .Where(t => !t.IsCategorized && t.TransferCounterpartId == null);

        if (filters.AccountId is Guid accountId)
        {
            query = query.Where(t => t.AccountId == accountId);
        }
        if (filters.From is DateOnly from)
        {
            query = query.Where(t => t.BookingDate >= from);
        }
        if (filters.To is DateOnly to)
        {
            query = query.Where(t => t.BookingDate <= to);
        }
        if (filters.Income is bool income)
        {
            query = income ? query.Where(t => t.Amount >= 0) : query.Where(t => t.Amount < 0);
        }
        if (!string.IsNullOrWhiteSpace(filters.SearchText))
        {
            var search = filters.SearchText.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Description.ToLower().Contains(search)
                || (t.Counterparty != null && t.Counterparty.ToLower().Contains(search))
            );
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new ReviewQueuePage(totalCount, items);
    }
}
