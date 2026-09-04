using AndreGoepel.FinanceApp.Components.Review;

namespace AndreGoepel.FinanceApp.Tests.Components.Review;

public sealed class ReviewPaginationTests
{
    [Theory]
    [InlineData(3, 100, 25, 3)]
    [InlineData(4, 100, 25, 3)]
    [InlineData(1, 25, 25, 0)]
    [InlineData(1, 0, 25, 0)]
    public void ClampPageIndex_ReturnsExistingPage(
        int currentPage,
        int totalCount,
        int pageSize,
        int expected
    ) => Assert.Equal(expected, ReviewPagination.ClampPageIndex(currentPage, totalCount, pageSize));
}
