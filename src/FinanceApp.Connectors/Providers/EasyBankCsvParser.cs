using FinanceApp.Connectors.Csv;
using FinanceApp.Connectors.Parsing;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// Easy Bank (AT) statement export: semicolon-separated, no header row, six
/// columns "IBAN;Text;Buchungsdatum;Valutadatum;Betrag;Währung", dates
/// dd.MM.yyyy, German decimals. Built against the documented export format.
/// </summary>
internal sealed class EasyBankCsvParser : IStatementParser
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy"];

    public string ParserId => "easybank-csv-v1";

    public ProviderKind Provider => ProviderKind.EasyBank;

    public bool CanParse(string content)
    {
        var first = CsvReader.Read(content, ';').FirstOrDefault();
        return first is { Fields.Count: 6 }
            && first.Fields[0].Trim().Length >= 15 // IBAN-like
            && char.IsLetter(first.Fields[0].Trim()[0])
            && FieldParser.TryParseDate(first.Fields[2], DateFormats, out _)
            && FieldParser.TryParseGermanDecimal(first.Fields[4], out _);
    }

    public StatementParseResult Parse(string content)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        foreach (var record in CsvReader.Read(content, ';'))
        {
            if (record.Fields.Count != 6)
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Expected 6 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseDate(record.Fields[2], DateFormats, out var bookingDate))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable booking date '{record.Fields[2]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            DateOnly? valueDate = null;
            if (FieldParser.TryParseDate(record.Fields[3], DateFormats, out var parsedValueDate))
            {
                valueDate = parsedValueDate;
            }

            if (!FieldParser.TryParseGermanDecimal(record.Fields[4], out var amount))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable amount '{record.Fields[4]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    valueDate,
                    amount,
                    record.Fields[5].Trim().ToUpperInvariant(),
                    Counterparty: null,
                    FieldParser.NullIfEmpty(record.Fields[1]) ?? "(no description)",
                    ExternalId: null,
                    record.RawLine
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }
}
