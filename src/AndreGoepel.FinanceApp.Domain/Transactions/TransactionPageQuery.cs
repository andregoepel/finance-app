using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

public sealed record TransactionPageFilters(
    IReadOnlyList<Guid>? AccountIds,
    IReadOnlyList<Guid>? CategoryIds,
    bool Uncategorized,
    DateOnly? From,
    DateOnly? To,
    string? SearchText
);

public enum TransactionSort
{
    BookingDate,
    Counterparty,
    Description,
    Amount,
}

public sealed record TransactionPage(int TotalCount, IReadOnlyList<TransactionView> Items);

public static class TransactionPageQuery
{
    public static async Task<TransactionPage> LoadAsync(
        IQuerySession session,
        TransactionPageFilters filters,
        TransactionSort sort,
        bool descending,
        int skip,
        int take,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        IQueryable<TransactionView> query = session.Query<TransactionView>();

        if (filters.AccountIds is { } filteredAccountIds)
        {
            var accountIds = filteredAccountIds.ToArray();
            query = query.Where(transaction => transaction.AccountId.IsOneOf(accountIds));
        }
        if (filters.Uncategorized)
        {
            query = query.Where(transaction => !transaction.IsCategorized);
        }
        else if (filters.CategoryIds is { Count: > 0 })
        {
            var categoryIds = filters.CategoryIds.ToArray();
            query = query.Where(transaction =>
                (
                    transaction.CategoryId != null
                    && transaction.CategoryId.Value.IsOneOf(categoryIds)
                ) || transaction.CategoryLines.Any(line => line.CategoryId.IsOneOf(categoryIds))
            );
        }
        if (filters.From is DateOnly from)
        {
            query = query.Where(transaction => transaction.BookingDate >= from);
        }
        if (filters.To is DateOnly to)
        {
            query = query.Where(transaction => transaction.BookingDate <= to);
        }
        if (!string.IsNullOrWhiteSpace(filters.SearchText))
        {
            var search = filters.SearchText.Trim().ToLowerInvariant();
            query = query.Where(transaction =>
                transaction.Description.ToLower().Contains(search)
                || (
                    transaction.Counterparty != null
                    && transaction.Counterparty.ToLower().Contains(search)
                )
            );
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = ApplySort(query, sort, descending);
        var items = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new TransactionPage(totalCount, items);
    }

    private static IOrderedQueryable<TransactionView> ApplySort(
        IQueryable<TransactionView> query,
        TransactionSort sort,
        bool descending
    ) =>
        (sort, descending) switch
        {
            (TransactionSort.Counterparty, false) => query
                .OrderBy(transaction => transaction.Counterparty)
                .ThenBy(transaction => transaction.Id),
            (TransactionSort.Counterparty, true) => query
                .OrderByDescending(transaction => transaction.Counterparty)
                .ThenByDescending(transaction => transaction.Id),
            (TransactionSort.Description, false) => query
                .OrderBy(transaction => transaction.Description)
                .ThenBy(transaction => transaction.Id),
            (TransactionSort.Description, true) => query
                .OrderByDescending(transaction => transaction.Description)
                .ThenByDescending(transaction => transaction.Id),
            (TransactionSort.Amount, false) => query
                .OrderBy(transaction => transaction.Amount)
                .ThenBy(transaction => transaction.Id),
            (TransactionSort.Amount, true) => query
                .OrderByDescending(transaction => transaction.Amount)
                .ThenByDescending(transaction => transaction.Id),
            (TransactionSort.BookingDate, false) => query
                .OrderBy(transaction => transaction.BookingDate)
                .ThenBy(transaction => transaction.Id),
            _ => query
                .OrderByDescending(transaction => transaction.BookingDate)
                .ThenByDescending(transaction => transaction.Id),
        };
}
