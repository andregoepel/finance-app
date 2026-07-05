namespace AndreGoepel.FinanceApp.Domain.Exchange;

/// <summary>
/// Supplies ECB reference rates for converting a foreign amount to EUR at a given
/// date. Network failures surface as a failed <see cref="Result"/>, never an
/// exception — conversion degrades gracefully and retries later.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>
    /// EUR per one unit of <paramref name="currency"/> on (or the nearest prior
    /// business day before) <paramref name="date"/>. <c>EUR</c> returns 1.
    /// </summary>
    Task<Result<EurRate>> GetEurRateAsync(
        string currency,
        DateOnly date,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// A resolved rate: <paramref name="EurPerUnit"/> EUR per one unit of the source
/// currency, effective on <paramref name="RateDate"/> (the actual ECB date used).
/// </summary>
public sealed record EurRate(decimal EurPerUnit, DateOnly RateDate);
