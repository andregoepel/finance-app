using FinanceApp.Connectors.Csv;
using FinanceApp.Connectors.Parsing;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// DKB Girokonto CSV export (2023+ banking format): semicolon-separated, a
/// preamble before the header row "Buchungsdatum";"Wertstellung";"Status";…,
/// dates dd.MM.yy, German decimals, EUR only. Pending rows (Status
/// "Vorgemerkt") are surfaced as skipped — they change their booking date when
/// they clear and would defeat deduplication. Built against the documented
/// export format.
/// </summary>
internal sealed class DkbCsvParser : IStatementParser
{
    private const string HeaderStart = "Buchungsdatum";

    public string ParserId => "dkb-csv-v1";

    public ProviderKind Provider => ProviderKind.Dkb;

    public bool CanParse(string content) =>
        content.Contains(
            "\"Buchungsdatum\";\"Wertstellung\";\"Status\"",
            StringComparison.OrdinalIgnoreCase
        );

    public StatementParseResult Parse(string content)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();
        var headerSeen = false;

        foreach (var record in CsvReader.Read(content, ';'))
        {
            if (!headerSeen)
            {
                headerSeen =
                    record.Fields.Count >= 9
                    && record
                        .Fields[0]
                        .Trim()
                        .Equals(HeaderStart, StringComparison.OrdinalIgnoreCase);
                continue; // preamble (account metadata) before the header
            }

            if (record.Fields.Count < 9)
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Expected at least 9 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            var status = record.Fields[2].Trim();
            if (!status.Equals("Gebucht", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Skipped: status is '{status}' (only booked rows are imported).",
                        record.RawLine
                    )
                );
                continue;
            }

            if (
                !FieldParser.TryParseDate(
                    record.Fields[0],
                    ["dd.MM.yy", "dd.MM.yyyy"],
                    out var bookingDate
                )
            )
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable booking date '{record.Fields[0]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            DateOnly? valueDate = null;
            if (
                FieldParser.TryParseDate(
                    record.Fields[1],
                    ["dd.MM.yy", "dd.MM.yyyy"],
                    out var parsedValueDate
                )
            )
            {
                valueDate = parsedValueDate;
            }

            if (!FieldParser.TryParseGermanDecimal(record.Fields[8], out var amount))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable amount '{record.Fields[8]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            var payer = FieldParser.NullIfEmpty(record.Fields[3]);
            var payee = FieldParser.NullIfEmpty(record.Fields[4]);
            var purpose = FieldParser.NullIfEmpty(record.Fields[5]);
            var transactionType = FieldParser.NullIfEmpty(record.Fields[6]);

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    valueDate,
                    amount,
                    "EUR",
                    Counterparty: amount < 0 ? payee : payer,
                    purpose ?? transactionType ?? "(no description)",
                    ExternalId: null,
                    record.RawLine
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }
}
