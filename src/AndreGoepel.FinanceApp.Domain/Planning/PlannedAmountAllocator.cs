namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>Allocates one booking across all linked occurrences, before filtering by month.</summary>
public static class PlannedAmountAllocator
{
    public static IReadOnlyDictionary<string, decimal?> Allocate(
        decimal? amountEur,
        IEnumerable<PlannedMatch> matches,
        IReadOnlyDictionary<Guid, PlannedItem> items
    )
    {
        var links = matches
            .Where(match => items.ContainsKey(match.PlannedItemId))
            .OrderBy(match => match.Id, StringComparer.Ordinal)
            .ToList();
        var total = links.Sum(match => Math.Abs(items[match.PlannedItemId].Amount));
        var result = new Dictionary<string, decimal?>();
        decimal allocated = 0;
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            decimal? share =
                amountEur is not decimal amount ? null
                : index == links.Count - 1 ? amount - allocated
                : total == 0 ? amount / links.Count
                : amount * (Math.Abs(items[link.PlannedItemId].Amount) / total);
            result[link.Id] = share;
            allocated += share ?? 0;
        }
        return result;
    }
}
