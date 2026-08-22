namespace AndreGoepel.FinanceApp;

/// <summary>
/// Date/time display formatting shared across the finance UI. Centralises the hardcoded German
/// formats (<c>dd.MM.yyyy</c> and friends) that were copied into several pages, so localizing the
/// app's copy doesn't leave the numeric date layout stuck in one culture while everything else
/// switches — see the localization plan's D-3 for why a fixed format (not <c>CurrentCulture</c>)
/// is the deliberate choice here.
/// </summary>
internal static class Dates
{
    /// <summary>Raw format string for <c>RadzenDatePicker.DateFormat</c>, which needs the literal pattern, not a formatted value.</summary>
    public const string ShortFormat = "dd.MM.yyyy";

    private const string ShortTimeFormat = "dd.MM.yyyy HH:mm";
    private const string LongFormat = "dddd, dd.MM.yyyy HH:mm";
    private const string DayMonthFormat = "dd.MM";
    private const string MonthFormat = "MMMM yyyy";
    private const string ChartMonthFormat = "MMM yy";

    public static string Short(DateOnly d) => d.ToString(ShortFormat);

    public static string Short(DateTime d) => d.ToString(ShortFormat);

    public static string Short(DateTimeOffset d) => d.ToString(ShortFormat);

    public static string? Short(DateTimeOffset? d) => d?.ToString(ShortFormat);

    public static string ShortTime(DateTime d) => d.ToString(ShortTimeFormat);

    public static string ShortTime(DateTimeOffset d) => d.ToString(ShortTimeFormat);

    public static string Long(DateTimeOffset d) => d.ToString(LongFormat);

    /// <summary>E.g. <c>"24.08"</c> — a near-term due date with no year, for compact badges.</summary>
    public static string DayMonth(DateOnly d) => d.ToString(DayMonthFormat);

    /// <summary>E.g. <c>"August 2026"</c> — the month-navigator heading.</summary>
    public static string Month(DateOnly d) => d.ToString(MonthFormat);

    /// <summary>E.g. <c>"Aug 26"</c> — compact axis label for the net-worth chart.</summary>
    public static string ChartMonth(DateOnly d) => d.ToString(ChartMonthFormat);
}
