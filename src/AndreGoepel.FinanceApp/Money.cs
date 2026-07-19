namespace AndreGoepel.FinanceApp;

/// <summary>
/// Money display formatting shared across the finance UI. Centralises the
/// <c>"{amount:N2} €"</c> pattern that was copied into several pages.
/// </summary>
internal static class Money
{
    /// <summary>Formats an amount as EUR, e.g. <c>"1,234.50 €"</c>.</summary>
    public static string Eur(decimal amount) => $"{amount:N2} €";

    /// <summary>
    /// Formats an amount with its currency: the € symbol for EUR (or an empty
    /// currency), otherwise the raw ISO code, e.g. <c>"12.34 CHF"</c>.
    /// </summary>
    public static string Format(decimal amount, string? currency) =>
        string.IsNullOrWhiteSpace(currency)
        || currency.Equals("EUR", StringComparison.OrdinalIgnoreCase)
            ? Eur(amount)
            : $"{amount:N2} {currency}";
}
