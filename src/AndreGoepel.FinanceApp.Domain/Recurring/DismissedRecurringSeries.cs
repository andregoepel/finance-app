namespace AndreGoepel.FinanceApp.Domain.Recurring;

/// <summary>
/// A recurring series the household has flagged as a false positive (not
/// actually recurring) — hidden from the Recurring page going forward.
/// <see cref="Id"/> is the series' <c>Counterparty</c> label, the same key
/// <c>RecurringDetector</c> groups by and <c>PlannedItem.CreatedFromRecurringKey</c>
/// uses, so dismissal survives the series being re-detected from scratch on
/// every page load (nothing about a <see cref="RecurringSeries"/> is persisted
/// otherwise).
/// </summary>
public sealed class DismissedRecurringSeries
{
    public required string Id { get; init; }

    public DateTimeOffset DismissedAt { get; init; } = DateTimeOffset.UtcNow;
}
