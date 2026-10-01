using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Planning;

internal sealed record MonthlyActualLine(Guid TransactionId, Guid? CategoryId, decimal? AmountEur);

internal sealed record MonthlyActuals(
    IReadOnlyList<MonthlyActualLine> Lines,
    IReadOnlyList<TransactionView> Transactions
)
{
    public static async Task<MonthlyActuals> LoadAsync(
        IQuerySession session,
        DateOnly start,
        DateOnly end,
        IReadOnlyCollection<Guid>? accountIds,
        CancellationToken cancellationToken
    )
    {
        var dueMatches = await session
            .Query<PlannedMatch>()
            .Where(match => match.DueDate >= start && match.DueDate < end)
            .ToListAsync(cancellationToken);
        var linkedIds = dueMatches.Select(match => match.TransactionId).Distinct().ToArray();
        var transactions = (
            await session
                .Query<TransactionView>()
                .Where(transaction =>
                    transaction.BookingDate >= start && transaction.BookingDate < end
                )
                .ToListAsync(cancellationToken)
        ).ToList();
        if (linkedIds.Length > 0)
        {
            transactions.AddRange(
                await session
                    .Query<TransactionView>()
                    .Where(transaction => transaction.Id.IsOneOf(linkedIds))
                    .ToListAsync(cancellationToken)
            );
        }
        transactions = transactions
            .DistinctBy(transaction => transaction.Id)
            .Where(transaction =>
                transaction.TransferCounterpartId is null
                && (accountIds is null || accountIds.Contains(transaction.AccountId))
            )
            .ToList();
        var ids = transactions.Select(transaction => transaction.Id).ToArray();
        var matches =
            ids.Length == 0
                ? []
                : await session
                    .Query<PlannedMatch>()
                    .Where(match => match.TransactionId.IsOneOf(ids))
                    .ToListAsync(cancellationToken);
        var itemIds = matches.Select(match => match.PlannedItemId).Distinct().ToArray();
        var items = (
            itemIds.Length == 0
                ? []
                : await session
                    .Query<PlannedItem>()
                    .Where(item => item.Id.IsOneOf(itemIds))
                    .ToListAsync(cancellationToken)
        ).ToDictionary(item => item.Id);
        var lines = BuildLines(transactions, matches, items, start, end);
        var includedIds = lines.Select(line => line.TransactionId).ToHashSet();
        return new MonthlyActuals(
            lines,
            transactions.Where(t => includedIds.Contains(t.Id)).ToList()
        );
    }

    internal static IReadOnlyList<MonthlyActualLine> BuildLines(
        IEnumerable<TransactionView> transactions,
        IEnumerable<PlannedMatch> matches,
        IReadOnlyDictionary<Guid, PlannedItem> items,
        DateOnly start,
        DateOnly end
    )
    {
        var byTransaction = matches.ToLookup(match => match.TransactionId);
        var result = new List<MonthlyActualLine>();
        foreach (var transaction in transactions.Where(t => t.TransferCounterpartId is null))
        {
            var links = byTransaction[transaction.Id]
                .Where(m => items.ContainsKey(m.PlannedItemId))
                .ToList();
            var shares = PlannedAmountAllocator.Allocate(transaction.AmountEur, links, items);
            if (links.Count > 0)
            {
                result.AddRange(
                    links
                        .Where(m => m.DueDate >= start && m.DueDate < end)
                        .Select(m => new MonthlyActualLine(
                            transaction.Id,
                            items[m.PlannedItemId].CategoryId,
                            shares[m.Id]
                        ))
                );
            }
            else if (transaction.BookingDate >= start && transaction.BookingDate < end)
            {
                if (transaction.EffectiveCategoryLines.Count == 0)
                    result.Add(new MonthlyActualLine(transaction.Id, null, transaction.AmountEur));
                else
                    result.AddRange(
                        transaction.EffectiveCategoryLines.Select(line => new MonthlyActualLine(
                            transaction.Id,
                            line.CategoryId,
                            transaction.EurAmountFor(line)
                        ))
                    );
            }
        }
        return result;
    }
}
