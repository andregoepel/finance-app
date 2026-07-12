using AndreGoepel.FinanceApp.Connectors.Csv;
using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers;

/// <summary>
/// Wise account statement CSV: 23 columns starting "TransferWise ID", "Date",
/// "Date Time", dates dd-MM-yyyy, invariant decimals. The Amount column is the
/// balance impact including fees (verified against Running Balance in a real
/// export). Counterparty: Merchant for card transactions, otherwise Payee
/// (DEBIT) / Payer (CREDIT). Built against a real export (2026-07).
/// </summary>
internal sealed class WiseCsvParser : IStatementParser
{
    private const int IdColumn = 0;
    private const int DateColumn = 1;
    private const int AmountColumn = 3;
    private const int CurrencyColumn = 4;
    private const int DescriptionColumn = 5;
    private const int PaymentReferenceColumn = 6;
    private const int ExchangeToColumn = 9;
    private const int PayerNameColumn = 11;
    private const int PayeeNameColumn = 12;
    private const int MerchantColumn = 14;
    private const int ExchangeToAmountColumn = 20;
    private const int TransactionTypeColumn = 21;

    public string ParserId => "wise-csv-v1";

    public ProviderKind Provider => ProviderKind.Wise;

    public bool CanParse(StatementFile file)
    {
        if (file.IsZipArchive)
        {
            return false;
        }

        var header = CsvReader.Read(file.DecodeText(), ',').FirstOrDefault();
        return header is { Fields.Count: >= 22 }
            && header.Fields[IdColumn].Trim() == "TransferWise ID"
            && header.Fields[DateColumn].Trim() == "Date"
            && header.Fields[2].Trim() == "Date Time";
    }

    public StatementParseResult Parse(StatementFile file)
    {
        var rows = new List<NormalizedTransaction>();
        var errors = new List<ImportRowError>();

        foreach (var record in CsvReader.Read(file.DecodeText(), ',').Skip(1))
        {
            if (record.Fields.Count < 22)
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Expected at least 22 columns, found {record.Fields.Count}.",
                        record.RawLine
                    )
                );
                continue;
            }

            if (
                !FieldParser.TryParseDate(
                    record.Fields[DateColumn],
                    ["dd-MM-yyyy"],
                    out var bookingDate
                )
            )
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable date '{record.Fields[DateColumn]}' (expected dd-MM-yyyy).",
                        record.RawLine
                    )
                );
                continue;
            }

            if (!FieldParser.TryParseInvariantDecimal(record.Fields[AmountColumn], out var amount))
            {
                errors.Add(
                    new ImportRowError(
                        record.LineNumber,
                        $"Unreadable amount '{record.Fields[AmountColumn]}'.",
                        record.RawLine
                    )
                );
                continue;
            }

            var description = FieldParser.NullIfEmpty(record.Fields[DescriptionColumn]);
            var paymentReference = FieldParser.NullIfEmpty(record.Fields[PaymentReferenceColumn]);
            var merchant = FieldParser.NullIfEmpty(record.Fields[MerchantColumn]);
            var payer = FieldParser.NullIfEmpty(record.Fields[PayerNameColumn]);
            var payee = FieldParser.NullIfEmpty(record.Fields[PayeeNameColumn]);
            var isDebit = record
                .Fields[TransactionTypeColumn]
                .Trim()
                .Equals("DEBIT", StringComparison.OrdinalIgnoreCase);

            var currency = record.Fields[CurrencyColumn].Trim().ToUpperInvariant();

            // Converted transactions carry the foreign leg in Exchange To /
            // Exchange To Amount (unsigned — mirror the booked sign). When
            // Exchange To equals the booked currency the foreign leg would be
            // Exchange From, but the export has no Exchange From Amount and
            // deriving it from Exchange Rate invites rounding drift — skip.
            decimal? originalAmount = null;
            string? originalCurrency = null;
            var exchangeTo = record.Fields[ExchangeToColumn].Trim().ToUpperInvariant();
            if (
                exchangeTo.Length > 0
                && exchangeTo != currency
                && FieldParser.TryParseInvariantDecimal(
                    record.Fields[ExchangeToAmountColumn],
                    out var exchangeToAmount
                )
            )
            {
                originalAmount =
                    amount < 0 ? -Math.Abs(exchangeToAmount) : Math.Abs(exchangeToAmount);
                originalCurrency = exchangeTo;
            }

            rows.Add(
                new NormalizedTransaction(
                    record.LineNumber,
                    bookingDate,
                    ValueDate: null,
                    amount,
                    currency,
                    Counterparty: merchant ?? (isDebit ? payee : payer),
                    description ?? paymentReference ?? "(no description)",
                    ExternalId: FieldParser.NullIfEmpty(record.Fields[IdColumn]),
                    record.RawLine,
                    OriginalAmount: originalAmount,
                    OriginalCurrency: originalCurrency
                )
            );
        }

        return new StatementParseResult(ParserId, rows, errors);
    }
}
