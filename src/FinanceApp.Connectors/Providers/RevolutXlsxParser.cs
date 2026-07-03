using FinanceApp.Connectors.Parsing;
using FinanceApp.Connectors.Xlsx;
using FinanceApp.Domain.Imports;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Providers;

/// <summary>
/// Revolut account statement XLSX (the app's only export format): columns
/// Type, Product, Started Date, Completed Date, Description, Amount, Fee,
/// Currency, State, Balance; dates as Excel serials. Only COMPLETED rows are
/// imported — pending/reverted rows are surfaced as skipped, never silently
/// dropped. The effective amount is Amount − Fee, matching the balance impact.
/// Built against a real export (2026-07).
/// </summary>
internal sealed class RevolutXlsxParser : IStatementParser
{
    private static readonly string[] HeaderColumns =
    [
        "A",
        "B",
        "C",
        "D",
        "E",
        "F",
        "G",
        "H",
        "I",
        "J",
    ];
    private static readonly string[] HeaderNames =
    [
        "Type",
        "Product",
        "Started Date",
        "Completed Date",
        "Description",
        "Amount",
        "Fee",
        "Currency",
        "State",
        "Balance",
    ];

    public string ParserId => "revolut-xlsx-v1";

    public ProviderKind Provider => ProviderKind.Revolut;

    public bool CanParse(StatementFile file)
    {
        if (!file.IsZipArchive || !XlsxReader.LooksLikeWorkbook(file.Content))
        {
            return false;
        }

        var header = XlsxReader.ReadFirstSheet(file.Content).FirstOrDefault();
        return header is not null && IsHeader(header);
    }

    public StatementParseResult Parse(StatementFile file)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        foreach (var row in XlsxReader.ReadFirstSheet(file.Content))
        {
            if (IsHeader(row) || row.Cells.Count == 0)
            {
                continue;
            }

            var raw = string.Join(";", HeaderColumns.Select(column => row.Cell(column)));

            var state = row.Cell("I").Trim().ToUpperInvariant();
            if (state != "COMPLETED")
            {
                errors.Add(
                    new ImportRowError(
                        row.RowNumber,
                        $"Skipped: state is {(state.Length == 0 ? "unknown" : state)} (only COMPLETED rows are imported).",
                        raw
                    )
                );
                continue;
            }

            if (
                !XlsxReader.TryParseOADate(row.Cell("D"), out var bookingDate)
                || !XlsxReader.TryParseOADate(row.Cell("C"), out var startedDate)
            )
            {
                errors.Add(
                    new ImportRowError(
                        row.RowNumber,
                        $"Unreadable date '{row.Cell("C")}' / '{row.Cell("D")}'.",
                        raw
                    )
                );
                continue;
            }

            if (
                !FieldParser.TryParseInvariantDecimal(row.Cell("F"), out var amount)
                || !FieldParser.TryParseInvariantDecimal(row.Cell("G"), out var fee)
            )
            {
                errors.Add(
                    new ImportRowError(
                        row.RowNumber,
                        $"Unreadable amount '{row.Cell("F")}' or fee '{row.Cell("G")}'.",
                        raw
                    )
                );
                continue;
            }

            var type = row.Cell("A").Trim();
            var description = FieldParser.NullIfEmpty(row.Cell("E")) ?? "(no description)";
            var isCardTransaction =
                type.Equals("Card Payment", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Card Refund", StringComparison.OrdinalIgnoreCase);

            rows.Add(
                new NormalizedTransaction(
                    row.RowNumber,
                    bookingDate,
                    ValueDate: startedDate,
                    amount - fee,
                    row.Cell("H").Trim().ToUpperInvariant(),
                    Counterparty: isCardTransaction ? description : null,
                    description,
                    ExternalId: null,
                    raw
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }

    private static bool IsHeader(XlsxRow row) =>
        HeaderColumns
            .Zip(HeaderNames)
            .All(pair =>
                row.Cell(pair.First).Trim().Equals(pair.Second, StringComparison.OrdinalIgnoreCase)
            );
}
