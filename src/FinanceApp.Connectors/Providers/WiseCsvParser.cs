using FinanceApp.Connectors.Csv;
using FinanceApp.Connectors.Parsing;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// Wise account statement CSV (comma-separated, header starting with
/// "TransferWise ID,Date,Amount,Currency,Description", dates dd-MM-yyyy,
/// invariant decimals). Built against the documented export format — refine
/// against a real anonymized export when available.
/// </summary>
internal sealed class WiseCsvParser : IStatementParser
{
    private const string HeaderSignature = "TransferWise ID,Date,Amount,Currency,Description";

    public string ParserId => "wise-csv-v1";

    public ProviderKind Provider => ProviderKind.Wise;

    public bool CanParse(string content) =>
        content.TrimStart().StartsWith(HeaderSignature, StringComparison.OrdinalIgnoreCase);

    public StatementParseResult Parse(string content)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        foreach (var record in CsvReader.Read(content, ',').Skip(1))
        {
            if (record.Fields.Count < 5)
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Expected at least 5 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseDate(record.Fields[1], ["dd-MM-yyyy"], out var bookingDate))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable date '{record.Fields[1]}' (expected dd-MM-yyyy).",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseInvariantDecimal(record.Fields[2], out var amount))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable amount '{record.Fields[2]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            var currency = record.Fields[3].Trim().ToUpperInvariant();
            var description = FieldParser.NullIfEmpty(record.Fields[4]);
            var paymentReference = Field(record, 5);
            var payerName = Field(record, 10);
            var payeeName = Field(record, 11);
            var merchant = Field(record, 13);

            var counterparty = merchant ?? (amount < 0 ? payeeName : payerName);

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    ValueDate: null,
                    amount,
                    currency,
                    counterparty,
                    description ?? paymentReference ?? "(no description)",
                    ExternalId: FieldParser.NullIfEmpty(record.Fields[0]),
                    record.RawLine
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }

    private static string? Field(CsvRecord record, int index) =>
        index < record.Fields.Count ? FieldParser.NullIfEmpty(record.Fields[index]) : null;
}
