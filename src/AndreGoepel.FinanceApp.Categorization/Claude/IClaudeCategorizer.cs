using AndreGoepel.Core;

namespace AndreGoepel.FinanceApp.Categorization.Claude;

/// <summary>
/// Claude API categorization fallback. Failures return <see cref="Result"/>
/// failures — never exceptions — so the pipeline degrades gracefully:
/// transactions simply stay in the review queue.
/// </summary>
public interface IClaudeCategorizer
{
    Task<Result<IReadOnlyList<ClaudeCategorySuggestion>>> SuggestAsync(
        IReadOnlyList<TransactionToCategorize> transactions,
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyList<FewShotExample> examples,
        CancellationToken cancellationToken = default
    );
}

/// <param name="RecurrenceHint">
/// One-line note when the counterparty forms a recurring series (see
/// <c>CategorizationHistory.RecurrenceHintFor</c>); <c>null</c> otherwise.
/// </param>
public sealed record TransactionToCategorize(
    Guid TransactionId,
    string? Counterparty,
    string Description,
    decimal Amount,
    string Currency,
    DateOnly? BookingDate = null,
    string? RecurrenceHint = null
);

/// <summary>A category the model may choose, with its display path (e.g. "Living › Groceries").</summary>
public sealed record CategoryOption(Guid Id, string Path);

/// <summary>Confirmed historical categorization used as a few-shot example.</summary>
public sealed record FewShotExample(
    string? Counterparty,
    string Description,
    decimal Amount,
    string CategoryPath
);

/// <summary><see cref="CategoryId"/> is <c>null</c> when the model declined to choose.</summary>
public sealed record ClaudeCategorySuggestion(
    Guid TransactionId,
    Guid? CategoryId,
    decimal Confidence
);
