using System.Text;
using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Xlsx;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers;

/// <summary>
/// Easy Bank credit card "Transaktionsansicht" XLSX (the bank's only export
/// format): an account metadata preamble, then a header row (Referenznummer,
/// Buchungsdatum ×2, Betrag, Beschreibung, Typ, Status, …). Amounts are German
/// strings with a € suffix ("−907,12 €"); column B is the transaction date,
/// column C the booking date (empty while pending). Pending rows (Status
/// "vorgemerkt") are surfaced as skipped — they would change on settlement and
/// defeat deduplication. Foreign-currency originals (Originalbetrag) are kept
/// in the description (verbatim, so dedup hashes stay stable) and additionally
/// captured as structured OriginalAmount/OriginalCurrency. Built against a
/// real export (2026-07).
/// </summary>
internal sealed class EasyBankXlsxParser : IStatementParser
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy"];

    public string ParserId => "easybank-xlsx-v1";

    public ProviderKind Provider => ProviderKind.EasyBank;

    public bool CanParse(StatementFile file)
    {
        if (!file.IsZipArchive || !XlsxReader.LooksLikeWorkbook(file.Content))
        {
            return false;
        }

        var sheet = XlsxReader.ReadFirstSheet(file.Content);
        return sheet.IsSuccess && sheet.Value!.Any(row => row.Cell("A").Trim() == "Referenznummer");
    }

    public StatementParseResult Parse(StatementFile file)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        var sheet = XlsxReader.ReadFirstSheet(file.Content);
        if (sheet.IsFailure)
        {
            errors.Add(new ImportRowError(0, sheet.Error!, null));
            return new StatementParseResult(ParserId, rows, errors);
        }

        var headerSeen = false;

        foreach (var row in sheet.Value!)
        {
            if (!headerSeen)
            {
                headerSeen = row.Cell("A").Trim() == "Referenznummer";
                continue; // account metadata preamble before the header
            }

            if (row.Cells.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var raw = string.Join(
                ";",
                new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "K", "O" }.Select(row.Cell)
            );

            var status = row.Cell("G").Trim();
            if (status.Equals("vorgemerkt", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    new ImportRowError(
                        row.RowNumber,
                        "Skipped: status is 'vorgemerkt' (only booked rows are imported).",
                        raw
                    )
                );
                continue;
            }

            var bookingDateText = FieldParser.NullIfEmpty(row.Cell("C")) ?? row.Cell("B");
            if (!FieldParser.TryParseDate(bookingDateText, DateFormats, out var bookingDate))
            {
                errors.Add(
                    new ImportRowError(
                        row.RowNumber,
                        $"Unreadable booking date '{bookingDateText}'.",
                        raw
                    )
                );
                continue;
            }

            DateOnly? valueDate = null;
            if (FieldParser.TryParseDate(row.Cell("B"), DateFormats, out var transactionDate))
            {
                valueDate = transactionDate;
            }

            if (!FieldParser.TryParseGermanEuroAmount(row.Cell("D"), out var amount))
            {
                errors.Add(
                    new ImportRowError(row.RowNumber, $"Unreadable amount '{row.Cell("D")}'.", raw)
                );
                continue;
            }

            var counterparty = FieldParser.NullIfEmpty(row.Cell("E"));
            var description = CollapseWhitespace(FieldParser.NullIfEmpty(row.Cell("O")) ?? "")
                is { Length: > 0 } details
                ? details
                : counterparty ?? "(no description)";

            // Foreign-currency purchases carry the original amount separately.
            // The description suffix must stay byte-identical — it feeds the
            // dedup hash of already-imported rows.
            decimal? originalForeignAmount = null;
            string? originalForeignCurrency = null;
            var originalAmount = FieldParser.NullIfEmpty(row.Cell("I"));
            if (originalAmount is not null && !originalAmount.EndsWith('€'))
            {
                description = $"{description} (Original: {originalAmount})";
                if (
                    FieldParser.TryParseAmountWithCurrency(
                        originalAmount,
                        out var parsedOriginal,
                        out var parsedCurrency
                    )
                    && parsedCurrency != "EUR"
                )
                {
                    originalForeignAmount = parsedOriginal;
                    originalForeignCurrency = parsedCurrency;
                }
            }

            rows.Add(
                new NormalizedTransaction(
                    row.RowNumber,
                    bookingDate,
                    valueDate,
                    amount,
                    "EUR",
                    counterparty,
                    description,
                    ExternalId: FieldParser.NullIfEmpty(row.Cell("A")),
                    raw,
                    OriginalAmount: originalForeignAmount,
                    OriginalCurrency: originalForeignCurrency
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(character);
        }
        return builder.ToString();
    }
}
