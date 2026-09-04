using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Domain.Exchange;

/// <summary>
/// Assigns EUR amounts to every transaction still missing one (non-EUR, not yet
/// converted). Idempotent — safe to publish after each import and on a schedule.
/// Rates are cached per (currency, date); a lookup failure (e.g. an unsupported
/// currency like a crypto asset, or the API being down) leaves that transaction
/// unconverted for a later run rather than failing.
/// </summary>
public sealed record ConvertPendingTransactionsToEurCommand;

public static class ConvertPendingTransactionsToEurCommandHandler
{
    public static async Task Handle(
        ConvertPendingTransactionsToEurCommand command,
        IDocumentSession session,
        IExchangeRateProvider rateProvider,
        ILogger<ConvertPendingTransactionsToEurCommand> logger,
        CancellationToken cancellationToken
    )
    {
        var pending = await session
            .Query<TransactionView>()
            .Where(t => t.AmountEur == null && t.Currency != "EUR")
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var group in pending.GroupBy(t => new { t.Currency, t.BookingDate }))
        {
            var rate = await ResolveRateAsync(
                session,
                rateProvider,
                group.Key.Currency,
                group.Key.BookingDate,
                logger,
                cancellationToken
            );
            if (rate is null)
            {
                continue; // leave unconverted; a later run retries
            }

            foreach (var transaction in group)
            {
                var stream = await session.Events.FetchForWriting<TransactionView>(
                    transaction.Id,
                    cancellationToken
                );
                if (stream.Aggregate is { AmountEur: null })
                {
                    // Store full precision; rounding happens only at display.
                    stream.AppendOne(
                        new TransactionEurAmountAssigned(
                            transaction.Amount * rate.EurPerUnit,
                            rate.EurPerUnit,
                            rate.RateDate
                        )
                    );
                }
            }

            try
            {
                await session.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrentUpdateException)
            {
                // This command is published after every account sync, so a burst of
                // syncs (e.g. "Sync All") can have two runs pick up the same pending
                // transaction and race to append its conversion event. Whichever loses
                // is simply skipped here rather than failing the whole message — the
                // winner already converted it, and anything genuinely still pending
                // gets picked up by the next publish.
                logger.LogInformation(
                    "Skipped a batch of {Currency} conversions on {Date}: a concurrent run already applied them.",
                    group.Key.Currency,
                    group.Key.BookingDate
                );
            }
        }
    }

    /// <summary>Rate from the cache, else fetched and cached; <c>null</c> when unavailable.</summary>
    private static async Task<EurRate?> ResolveRateAsync(
        IDocumentSession session,
        IExchangeRateProvider rateProvider,
        string currency,
        DateOnly date,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var key = ExchangeRate.KeyFor(currency, date);
        var cached = await session.LoadAsync<ExchangeRate>(key, cancellationToken);
        if (cached is not null)
        {
            return new EurRate(cached.EurPerUnit, cached.RateDate);
        }

        var fetched = await rateProvider.GetEurRateAsync(currency, date, cancellationToken);
        if (fetched.IsFailure)
        {
            logger.LogWarning(
                "No EUR rate for {Currency} on {Date}: {Reason}",
                currency,
                date,
                fetched.Error
            );
            return null;
        }

        session.Store(
            new ExchangeRate
            {
                Id = key,
                Currency = currency,
                RequestedDate = date,
                RateDate = fetched.Value!.RateDate,
                EurPerUnit = fetched.Value.EurPerUnit,
            }
        );
        return fetched.Value;
    }
}
