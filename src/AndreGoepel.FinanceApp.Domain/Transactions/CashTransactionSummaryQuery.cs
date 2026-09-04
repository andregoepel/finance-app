using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

public sealed record CashTransactionSummary(decimal Spent, decimal Received);

public static class CashTransactionSummaryQuery
{
    public static async Task<CashTransactionSummary> LoadAsync(
        IQuerySession session,
        Guid accountId,
        DateOnly monthStart,
        CancellationToken cancellationToken = default
    )
    {
        var nextMonth = monthStart.AddMonths(1);
        var monthly = session
            .Query<TransactionView>()
            .Where(transaction =>
                transaction.AccountId == accountId
                && transaction.BookingDate >= monthStart
                && transaction.BookingDate < nextMonth
            );
        var spent = await monthly
            .Where(transaction => transaction.Amount < 0)
            .SumAsync(transaction => transaction.Amount, cancellationToken);
        var received = await monthly
            .Where(transaction => transaction.Amount > 0)
            .SumAsync(transaction => transaction.Amount, cancellationToken);

        return new CashTransactionSummary(-spent, received);
    }
}
