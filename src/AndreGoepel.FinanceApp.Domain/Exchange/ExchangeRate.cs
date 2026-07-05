namespace AndreGoepel.FinanceApp.Domain.Exchange;

/// <summary>
/// A cached ECB rate — one document per (currency, requested date). Rates are
/// immutable history, so once stored they are reused instead of re-fetched,
/// which also lets conversion run offline after the first lookup.
/// </summary>
public sealed class ExchangeRate
{
    /// <summary>Document id: <c>{currency}:{requestedDate:yyyy-MM-dd}</c>.</summary>
    public required string Id { get; init; }

    public required string Currency { get; init; }

    /// <summary>The booking date the rate was requested for.</summary>
    public required DateOnly RequestedDate { get; init; }

    /// <summary>The actual ECB date the rate is from (prior business day on weekends/holidays).</summary>
    public required DateOnly RateDate { get; init; }

    public required decimal EurPerUnit { get; init; }

    public static string KeyFor(string currency, DateOnly requestedDate) =>
        $"{currency}:{requestedDate:yyyy-MM-dd}";
}
