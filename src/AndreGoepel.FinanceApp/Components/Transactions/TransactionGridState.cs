using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Components.Transactions;

internal static class TransactionGridState
{
    internal static (TransactionSort Sort, bool Descending) ParseSort(string? orderBy)
    {
        var parts = orderBy?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (
            parts is null
            || parts.Length == 0
            || !Enum.TryParse<TransactionSort>(parts[0], ignoreCase: true, out var sort)
        )
        {
            return (TransactionSort.BookingDate, true);
        }

        return (
            sort,
            parts.Skip(1).Any(part => part.Equals("desc", StringComparison.OrdinalIgnoreCase))
        );
    }

    internal static int ClampPageIndex(int currentPage, int totalCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentPage);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var lastPage = totalCount == 0 ? 0 : (totalCount - 1) / pageSize;
        return Math.Min(currentPage, lastPage);
    }
}
