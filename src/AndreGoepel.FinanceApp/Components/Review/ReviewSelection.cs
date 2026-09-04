using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Components.Review;

internal static class ReviewSelection
{
    internal static IList<TransactionView> MergeCurrentPage(
        IEnumerable<TransactionView> selected,
        IEnumerable<TransactionView> currentPage
    )
    {
        var merged = selected.ToDictionary(transaction => transaction.Id);
        foreach (var transaction in currentPage)
        {
            merged[transaction.Id] = transaction;
        }

        return merged.Values.ToList();
    }

    internal static bool IsEntirePageSelected(
        IEnumerable<TransactionView> selected,
        IReadOnlyCollection<TransactionView> currentPage
    )
    {
        if (currentPage.Count == 0)
        {
            return true;
        }

        var selectedIds = selected.Select(transaction => transaction.Id).ToHashSet();
        return currentPage.All(transaction => selectedIds.Contains(transaction.Id));
    }
}
