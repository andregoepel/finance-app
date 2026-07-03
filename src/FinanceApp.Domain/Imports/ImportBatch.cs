using FinanceApp.Domain.Providers;

namespace FinanceApp.Domain.Imports;

/// <summary>
/// Audit record of one import run (file upload or API sync). Parser failures
/// are never dropped silently — every rejected row is listed with its row
/// number so the import UI can surface it.
/// </summary>
public sealed class ImportBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid AccountId { get; init; }

    public required ProviderKind Provider { get; init; }

    /// <summary>Uploaded file name (or sync source identifier in Phase 3).</summary>
    public required string Source { get; init; }

    /// <summary>Identifier of the versioned parser that read the file (e.g. <c>wise-csv-v1</c>).</summary>
    public required string ParserId { get; init; }

    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ImportedBy { get; init; }

    public int TotalRows { get; init; }

    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int ErrorCount => Errors.Count;

    public IReadOnlyList<ImportRowError> Errors { get; init; } = [];
}

/// <summary>A row the parser could not read — surfaced in the import UI.</summary>
public sealed record ImportRowError(int RowNumber, string Message, string? RawLine);
