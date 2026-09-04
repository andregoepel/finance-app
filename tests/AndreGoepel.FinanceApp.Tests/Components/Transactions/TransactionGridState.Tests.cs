using AndreGoepel.FinanceApp.Components.Transactions;
using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Tests.Components.Transactions;

public sealed class TransactionGridStateTests
{
    [Theory]
    [InlineData(null, TransactionSort.BookingDate, true)]
    [InlineData("", TransactionSort.BookingDate, true)]
    [InlineData("BookingDate desc", TransactionSort.BookingDate, true)]
    [InlineData("Counterparty asc", TransactionSort.Counterparty, false)]
    [InlineData("Description DESC", TransactionSort.Description, true)]
    [InlineData("Amount", TransactionSort.Amount, false)]
    [InlineData("Unsupported desc", TransactionSort.BookingDate, true)]
    public void ParseSort_ReturnsSupportedSort(
        string? orderBy,
        TransactionSort expectedSort,
        bool expectedDescending
    ) => Assert.Equal((expectedSort, expectedDescending), TransactionGridState.ParseSort(orderBy));

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
    ) =>
        Assert.Equal(
            expected,
            TransactionGridState.ClampPageIndex(currentPage, totalCount, pageSize)
        );
}
