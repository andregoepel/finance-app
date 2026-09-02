namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <summary>
/// A planned cost or income the household maintains (the Excel replacement). It
/// expands into monthly occurrences that are matched against actual transactions.
/// <see cref="Amount"/> is signed EUR — negative for expenses, positive for income.
/// </summary>
public sealed class PlannedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Description { get; set; }

    /// <summary>Signed EUR amount: negative = expense, positive = income.</summary>
    public required decimal Amount { get; set; }

    public Guid? CategoryId { get; set; }

    public required PlannedSchedule Schedule { get; set; }

    /// <summary>Account the item is expected on, when known — narrows auto-matching.</summary>
    public Guid? ExpectedAccountId { get; set; }

    /// <summary>Case-insensitive substring the counterparty/description should contain.</summary>
    public string? CounterpartyPattern { get; set; }

    /// <summary>Allowed amount deviation as a fraction of the planned amount (e.g. 0.1 = ±10%).</summary>
    public decimal AmountTolerance { get; set; } = 0.1m;

    /// <summary>How many days around the due date a matching transaction may fall.</summary>
    public int DateWindowDays { get; set; } = 7;

    public bool Active { get; set; } = true;

    /// <summary>
    /// The detected recurring series this item was created from, if any — the
    /// series key, which is the counterparty the detector grouped on. Provenance
    /// only: it records where the item came from and is never rewritten when the
    /// item is edited, so the Recurring page can tell that a series has already
    /// been taken over even after its description or pattern was changed. Null
    /// for items entered by hand and for anything created before this existed.
    /// </summary>
    public string? CreatedFromRecurringKey { get; set; }
}

/// <summary>
/// When a planned item recurs. For one-time items only <see cref="StartDate"/>
/// applies; for recurring items occurrences step from <see cref="StartDate"/> and
/// stop at <see cref="EndDate"/> (open-ended when null).
/// </summary>
public sealed record PlannedSchedule(
    PlannedFrequency Frequency,
    DateOnly StartDate,
    DateOnly? EndDate = null
);

public enum PlannedFrequency
{
    OneTime,
    Monthly,
    Quarterly,
    Yearly,
}
