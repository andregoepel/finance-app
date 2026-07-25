namespace AndreGoepel.FinanceApp.Domain.Recurring;

/// <summary>
/// Heuristic detection of recurring payments/income: groups transactions by
/// counterparty and reports groups that repeat at a regular interval with a
/// consistent amount (subscriptions, salary, rent, utilities). Pure and
/// side-effect-free so it is easy to test and to tune.
/// </summary>
public static class RecurringDetector
{
    private const int MinOccurrences = 3;

    // Share of gaps that must match the interval for it to count as regular.
    private const double RegularityThreshold = 0.6;

    // How far each amount may stray from the median magnitude (±35%).
    private const decimal AmountTolerance = 0.35m;

    public static IReadOnlyList<RecurringSeries> Detect(
        IReadOnlyList<RecurringCandidate> candidates
    )
    {
        var series = new List<RecurringSeries>();

        foreach (var group in candidates.GroupBy(c => Normalize(c.Counterparty)))
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                continue;
            }

            var items = group.OrderBy(c => c.Date).ToList();
            var dates = items.Select(c => c.Date).Distinct().OrderBy(d => d).ToList();
            if (dates.Count < MinOccurrences)
            {
                continue;
            }

            var gaps = dates.Zip(dates.Skip(1), (a, b) => b.DayNumber - a.DayNumber).ToList();
            if (Classify(Median(gaps)) is not RecurrenceInterval interval)
            {
                continue;
            }

            var matching = gaps.Count(g => Classify(g) == interval);
            if (matching < Math.Ceiling(gaps.Count * RegularityThreshold))
            {
                continue;
            }

            var amounts = items.Select(c => c.Amount).ToList();
            var typical = Median(amounts);
            if (!AmountsConsistent(amounts, typical))
            {
                continue;
            }

            var lastSeen = dates[^1];
            series.Add(
                new RecurringSeries(
                    MostCommonLabel(items),
                    typical,
                    interval,
                    items.Count,
                    lastSeen,
                    NextAfter(lastSeen, interval)
                )
            );
        }

        return series.OrderBy(s => s.NextExpected).ToList();
    }

    private static bool AmountsConsistent(IReadOnlyList<decimal> amounts, decimal median)
    {
        if (median == 0)
        {
            return false;
        }
        var magnitude = Math.Abs(median);
        var low = magnitude * (1 - AmountTolerance);
        var high = magnitude * (1 + AmountTolerance);
        return amounts.All(a =>
            Math.Sign(a) == Math.Sign(median) && Math.Abs(a) >= low && Math.Abs(a) <= high
        );
    }

    private static RecurrenceInterval? Classify(int days) =>
        days switch
        {
            >= 5 and <= 9 => RecurrenceInterval.Weekly,
            >= 11 and <= 17 => RecurrenceInterval.Biweekly,
            >= 25 and <= 38 => RecurrenceInterval.Monthly,
            >= 80 and <= 100 => RecurrenceInterval.Quarterly,
            >= 330 and <= 400 => RecurrenceInterval.Yearly,
            _ => null,
        };

    private static DateOnly NextAfter(DateOnly date, RecurrenceInterval interval) =>
        interval switch
        {
            RecurrenceInterval.Weekly => date.AddDays(7),
            RecurrenceInterval.Biweekly => date.AddDays(14),
            RecurrenceInterval.Monthly => date.AddMonths(1),
            RecurrenceInterval.Quarterly => date.AddMonths(3),
            RecurrenceInterval.Yearly => date.AddYears(1),
            _ => date,
        };

    private static string MostCommonLabel(IReadOnlyList<RecurringCandidate> items) =>
        items.GroupBy(c => c.Counterparty.Trim()).OrderByDescending(g => g.Count()).First().Key;

    private static string Normalize(string counterparty) =>
        string.Join(
            ' ',
            counterparty
                .ToLowerInvariant()
                .Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)
        );

    private static int Median(IReadOnlyList<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}

/// <summary>One transaction considered for recurrence: its counterparty, date and signed EUR amount.</summary>
public sealed record RecurringCandidate(string Counterparty, DateOnly Date, decimal Amount);

/// <summary>A detected recurring payment or income stream.</summary>
public sealed record RecurringSeries(
    string Counterparty,
    decimal TypicalAmount,
    RecurrenceInterval Interval,
    int Occurrences,
    DateOnly LastSeen,
    DateOnly NextExpected
);

public enum RecurrenceInterval
{
    Weekly,
    Biweekly,
    Monthly,
    Quarterly,
    Yearly,
}
