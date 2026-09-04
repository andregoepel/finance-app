namespace AndreGoepel.FinanceApp.Components.Review;

internal static class ReviewPagination
{
    internal static int ClampPageIndex(int currentPage, int totalCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentPage);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var lastPage = totalCount == 0 ? 0 : (totalCount - 1) / pageSize;
        return Math.Min(currentPage, lastPage);
    }
}
