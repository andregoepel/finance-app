using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Parsing;

/// <summary>
/// Parses one provider export format (versioned — a format change gets a new
/// parser id, never a silent adjustment). Rows that cannot be read become
/// <see cref="ImportRowError"/>s with row numbers; rows are never dropped
/// silently.
/// </summary>
public interface IStatementParser
{
    /// <summary>Stable format identifier, e.g. <c>wise-csv-v1</c> — recorded on every <c>ImportBatch</c>.</summary>
    string ParserId { get; }

    ProviderKind Provider { get; }

    /// <summary>Cheap signature check (header/shape) — used to select the format version.</summary>
    bool CanParse(StatementFile file);

    StatementParseResult Parse(StatementFile file);
}

public sealed record StatementParseResult(
    string ParserId,
    IReadOnlyList<NormalizedTransaction> Rows,
    IReadOnlyList<ImportRowError> Errors
);
