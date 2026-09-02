using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Recurring;
using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Categorization.History;

/// <summary>
/// One transaction of the household, reduced to what history-aware
/// categorization needs. Loaded as a Marten <c>Select</c> projection so a full
/// history scan stays cheap.
/// </summary>
public sealed class HistoryEntry
{
    public Guid Id { get; init; }

    public string? Counterparty { get; init; }

    public string Description { get; init; } = "";

    public decimal Amount { get; init; }

    public decimal? AmountEur { get; init; }

    public DateOnly BookingDate { get; init; }

    public Guid? CategoryId { get; init; }

    public CategorySource? CategorySource { get; init; }
}

/// <summary>
/// What the household's own history says about a counterparty — the cheap,
/// deterministic knowledge the pipeline consults before (and hands to) Claude:
/// <list type="bullet">
/// <item><see cref="ConsistentCategoryFor"/>: a counterparty confirmed by hand
/// at least twice, always with the same category, needs no model call.</item>
/// <item><see cref="ExamplesFor"/>: few-shot examples chosen for the batch at
/// hand (same counterparties first) instead of whatever was confirmed most
/// recently.</item>
/// <item><see cref="RecurrenceHintFor"/>: a recurring series on the counterparty
/// (via <see cref="RecurringDetector"/>) — the difference between a monthly
/// insurance premium and a one-off doctor's bill.</item>
/// </list>
/// Only <see cref="Domain.Transactions.CategorySource.Manual"/> categorizations
/// count as confirmed (that includes accepted review suggestions and
/// corrections); the pipeline's own rule/AI/history decisions never feed back
/// into themselves.
/// </summary>
public sealed class CategorizationHistory
{
    internal const int MinConsistentOccurrences = 2;
    internal const int MaxExamplesPerCounterparty = 2;

    private readonly Dictionary<string, List<HistoryEntry>> _byCounterparty;
    private readonly List<HistoryEntry> _confirmedNewestFirst;
    private readonly IReadOnlyDictionary<Guid, string> _paths;
    private readonly Dictionary<string, string?> _recurrenceCache = [];

    public CategorizationHistory(
        IReadOnlyList<HistoryEntry> entries,
        IReadOnlyList<CategoryOption> categories
    )
    {
        _paths = categories.ToDictionary(category => category.Id, category => category.Path);
        _byCounterparty = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Counterparty))
            .GroupBy(entry => Normalize(entry.Counterparty!))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => entry.BookingDate).ToList()
            );
        _confirmedNewestFirst = entries
            .Where(IsConfirmed)
            .OrderByDescending(entry => entry.BookingDate)
            .ToList();
    }

    /// <summary>
    /// The category this household has confirmed for the counterparty, when it
    /// was confirmed at least <see cref="MinConsistentOccurrences"/> times and
    /// never with a different category. Conflicting history returns
    /// <c>null</c> and leaves the decision to Claude.
    /// </summary>
    public Guid? ConsistentCategoryFor(string? counterparty)
    {
        if (!TryGetEntries(counterparty, out var entries))
        {
            return null;
        }

        var confirmed = entries.Where(IsConfirmed).ToList();
        if (confirmed.Count < MinConsistentOccurrences)
        {
            return null;
        }

        var categoryId = confirmed[0].CategoryId!.Value;
        return confirmed.All(entry => entry.CategoryId == categoryId) ? categoryId : null;
    }

    /// <summary>
    /// Few-shot examples for a batch: up to <see cref="MaxExamplesPerCounterparty"/>
    /// confirmed examples for every counterparty in the batch, then the most
    /// recently confirmed transactions overall until <paramref name="maxTotal"/>
    /// is reached. Counterparty-specific examples are never cut in favour of
    /// the recent fill.
    /// </summary>
    public IReadOnlyList<FewShotExample> ExamplesFor(
        IEnumerable<string?> counterparties,
        int maxTotal
    )
    {
        var picked = new List<HistoryEntry>();
        var seen = new HashSet<Guid>();

        foreach (var counterparty in counterparties)
        {
            if (!TryGetEntries(counterparty, out var entries))
            {
                continue;
            }
            foreach (var entry in entries.Where(IsConfirmed).Take(MaxExamplesPerCounterparty))
            {
                if (seen.Add(entry.Id))
                {
                    picked.Add(entry);
                }
            }
        }

        foreach (var entry in _confirmedNewestFirst)
        {
            if (picked.Count >= maxTotal)
            {
                break;
            }
            if (seen.Add(entry.Id))
            {
                picked.Add(entry);
            }
        }

        return picked
            .Select(entry => new FewShotExample(
                entry.Counterparty,
                entry.Description,
                entry.Amount,
                _paths[entry.CategoryId!.Value]
            ))
            .ToList();
    }

    /// <summary>
    /// A one-line note when the counterparty forms a recurring series (regular
    /// interval, consistent amount), otherwise <c>null</c>. Uses the same
    /// detector as the Recurring page so both agree on what "recurring" means.
    /// </summary>
    public string? RecurrenceHintFor(string? counterparty)
    {
        if (!TryGetEntries(counterparty, out var entries))
        {
            return null;
        }

        var key = Normalize(counterparty!);
        if (_recurrenceCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var candidates = entries
            .Select(entry => new RecurringCandidate(
                entry.Counterparty!,
                entry.BookingDate,
                entry.AmountEur ?? entry.Amount
            ))
            .ToList();
        var series = RecurringDetector.Detect(candidates).FirstOrDefault();
        var hint = series is null
            ? null
            : $"recurs {Describe(series.Interval)} with a consistent amount "
                + $"(typically {series.TypicalAmount:0.00} EUR, {series.Occurrences} occurrences, "
                + $"last seen {series.LastSeen:yyyy-MM-dd})";
        _recurrenceCache[key] = hint;
        return hint;
    }

    private bool IsConfirmed(HistoryEntry entry) =>
        entry.CategorySource == Domain.Transactions.CategorySource.Manual
        && entry.CategoryId is Guid categoryId
        && _paths.ContainsKey(categoryId);

    private bool TryGetEntries(string? counterparty, out List<HistoryEntry> entries)
    {
        entries = [];
        return !string.IsNullOrWhiteSpace(counterparty)
            && _byCounterparty.TryGetValue(Normalize(counterparty), out entries!);
    }

    private static string Normalize(string counterparty) =>
        TextNormalization.NormalizeWhitespace(counterparty);

    private static string Describe(RecurrenceInterval interval) =>
        interval switch
        {
            RecurrenceInterval.Weekly => "weekly",
            RecurrenceInterval.Biweekly => "every two weeks",
            RecurrenceInterval.Monthly => "monthly",
            RecurrenceInterval.Quarterly => "quarterly",
            RecurrenceInterval.Yearly => "yearly",
            _ => interval.ToString().ToLowerInvariant(),
        };
}
