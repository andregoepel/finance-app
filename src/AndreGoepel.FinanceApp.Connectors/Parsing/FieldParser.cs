using System.Globalization;
using System.Text.RegularExpressions;

namespace AndreGoepel.FinanceApp.Connectors.Parsing;

/// <summary>Culture-aware field parsing shared by the statement parsers.</summary>
internal static partial class FieldParser
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static bool TryParseDate(string value, string[] formats, out DateOnly date) =>
        DateOnly.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );

    /// <summary>Timestamp like <c>2026-01-31 14:05:22</c>; only the date part is kept.</summary>
    public static bool TryParseTimestampDate(string value, out DateOnly date)
    {
        if (
            DateTime.TryParseExact(
                value.Trim(),
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp
            )
        )
        {
            date = DateOnly.FromDateTime(timestamp);
            return true;
        }
        date = default;
        return false;
    }

    /// <summary>Invariant decimal, e.g. <c>-1234.56</c>. Money is decimal only.</summary>
    public static bool TryParseInvariantDecimal(string value, out decimal amount) =>
        decimal.TryParse(
            NormalizeMinus(value),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount
        );

    /// <summary>German decimal, e.g. <c>-1.234,56</c> (DKB, Easy Bank).</summary>
    public static bool TryParseGermanDecimal(string value, out decimal amount) =>
        decimal.TryParse(NormalizeMinus(value), NumberStyles.Number, German, out amount);

    /// <summary>German currency string with € suffix, e.g. <c>+1.500,00 €</c> (Easy Bank).</summary>
    public static bool TryParseGermanEuroAmount(string value, out decimal amount) =>
        TryParseGermanDecimal(value.Replace("€", "").Replace(' ', ' '), out amount);

    /// <summary>
    /// German amount followed by a three-letter currency code, e.g.
    /// <c>−25,00 USD</c> (Easy Bank Originalbetrag). Fails for €-suffixed
    /// values, missing codes and unreadable numbers.
    /// </summary>
    public static bool TryParseAmountWithCurrency(
        string value,
        out decimal amount,
        out string currency
    )
    {
        amount = 0;
        currency = "";

        var match = AmountWithCurrencyPattern().Match(value.Trim());
        if (!match.Success || !TryParseGermanDecimal(match.Groups["amount"].Value, out amount))
        {
            return false;
        }

        currency = match.Groups["currency"].Value.ToUpperInvariant();
        return true;
    }

    // Splits an amount from a trailing three-letter currency code: the number then whitespace then exactly three ASCII letters. A € suffix does not match.
    [GeneratedRegex(@"^(?<amount>\S.*?)\s+(?<currency>[A-Za-z]{3})$")]
    private static partial Regex AmountWithCurrencyPattern();

    // Some exports use the typographic minus (U+2212) instead of a hyphen.
    private static string NormalizeMinus(string value) => value.Trim().Replace('−', '-');

    public static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
