using FinanceApp.Connectors.Csv;
using FinanceApp.Connectors.Parsing;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// Revolut account statement CSV ("Type,Product,Started Date,Completed Date,
/// Description,Amount,Fee,Currency,State,Balance"). Only COMPLETED rows are
/// imported — pending/reverted rows are surfaced as skipped, never silently
/// dropped (a pending booking would change its date on completion and defeat
/// deduplication). The effective amount is Amount − Fee, matching the balance
/// impact. Built against the documented export format.
/// </summary>
internal sealed class RevolutCsvParser : IStatementParser
{
    private const string HeaderSignature =
        "Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance";

    public string ParserId => "revolut-csv-v1";

    public ProviderKind Provider => ProviderKind.Revolut;

    public bool CanParse(string content) =>
        content.TrimStart().StartsWith(HeaderSignature, StringComparison.OrdinalIgnoreCase);

    public StatementParseResult Parse(string content)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        foreach (var record in CsvReader.Read(content, ',').Skip(1))
        {
            if (record.Fields.Count < 10)
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Expected 10 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            var state = record.Fields[8].Trim().ToUpperInvariant();
            if (state != "COMPLETED")
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Skipped: state is {state} (only COMPLETED rows are imported).",
                        record.RawLine
                    )
                );
                continue;
            }

            if (
                !FieldParser.TryParseTimestampDate(record.Fields[3], out var bookingDate)
                || !FieldParser.TryParseTimestampDate(record.Fields[2], out var startedDate)
            )
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable date '{record.Fields[2]}' / '{record.Fields[3]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (
                !FieldParser.TryParseInvariantDecimal(record.Fields[5], out var amount)
                || !FieldParser.TryParseInvariantDecimal(record.Fields[6], out var fee)
            )
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable amount '{record.Fields[5]}' or fee '{record.Fields[6]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            var type = record.Fields[0].Trim().ToUpperInvariant();
            var description = FieldParser.NullIfEmpty(record.Fields[4]) ?? "(no description)";

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    ValueDate: startedDate,
                    amount - fee,
                    record.Fields[7].Trim().ToUpperInvariant(),
                    Counterparty: type == "CARD_PAYMENT" ? description : null,
                    description,
                    ExternalId: null,
                    record.RawLine
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }
}
