using FinanceApp.Connectors.Csv;
using FinanceApp.Connectors.Parsing;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// Crypto.com app transaction export ("Timestamp (UTC),Transaction Description,
/// Currency,Amount,To Currency,To Amount,Native Currency,Native Amount,
/// Native Amount (in USD),Transaction Kind,Transaction Hash"). The normalized
/// amount is the native (fiat) amount — what the transaction cost in the
/// account currency; the crypto asset and quantity are kept in the description.
/// Portfolio valuation by daily prices is Phase 4. Built against the documented
/// export format.
/// </summary>
internal sealed class CryptoComCsvParser : IStatementParser
{
    private const string HeaderSignature = "Timestamp (UTC),Transaction Description,Currency";

    public string ParserId => "cryptocom-csv-v1";

    public ProviderKind Provider => ProviderKind.CryptoCom;

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
                        $"Expected at least 10 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseTimestampDate(record.Fields[0], out var bookingDate))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable timestamp '{record.Fields[0]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseInvariantDecimal(record.Fields[7], out var nativeAmount))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable native amount '{record.Fields[7]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            var description = FieldParser.NullIfEmpty(record.Fields[1]) ?? "(no description)";
            var assetCurrency = FieldParser.NullIfEmpty(record.Fields[2]);
            var assetAmount = FieldParser.NullIfEmpty(record.Fields[3]);
            if (assetCurrency is not null && assetAmount is not null)
            {
                description = $"{description} ({assetAmount} {assetCurrency})";
            }

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    ValueDate: null,
                    nativeAmount,
                    record.Fields[6].Trim().ToUpperInvariant(),
                    Counterparty: null,
                    description,
                    ExternalId: Field(record, 10),
                    record.RawLine
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }

    private static string? Field(CsvRecord record, int index) =>
        index < record.Fields.Count ? FieldParser.NullIfEmpty(record.Fields[index]) : null;
}
