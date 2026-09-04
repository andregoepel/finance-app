using AndreGoepel.FinanceApp.Components.Review;
using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Tests.Components.Review;

public sealed class ReviewSelectionTests
{
    [Fact]
    public void MergeCurrentPage_PreservesSelectionsFromOtherPages()
    {
        var previousPage = Transaction();
        var currentPage = new[] { Transaction(), Transaction() };

        var result = ReviewSelection.MergeCurrentPage([previousPage], currentPage);

        Assert.Equal(3, result.Count);
        Assert.Contains(previousPage, result);
        Assert.All(currentPage, transaction => Assert.Contains(transaction, result));
    }

    [Fact]
    public void MergeCurrentPage_ReplacesPreviouslyLoadedInstanceWithSameId()
    {
        var id = Guid.NewGuid();
        var stale = Transaction(id);
        var current = Transaction(id);

        var result = ReviewSelection.MergeCurrentPage([stale], [current]);

        Assert.Single(result);
        Assert.Same(current, result[0]);
    }

    [Fact]
    public void IsEntirePageSelected_RequiresEveryCurrentPageItem()
    {
        var first = Transaction();
        var second = Transaction();

        Assert.False(ReviewSelection.IsEntirePageSelected([first], [first, second]));
        Assert.True(ReviewSelection.IsEntirePageSelected([first, second], [first, second]));
    }

    [Fact]
    public void IsEntirePageSelected_ReturnsTrueForEmptyPage() =>
        Assert.True(ReviewSelection.IsEntirePageSelected([], []));

    private static TransactionView Transaction(Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid() };
}
