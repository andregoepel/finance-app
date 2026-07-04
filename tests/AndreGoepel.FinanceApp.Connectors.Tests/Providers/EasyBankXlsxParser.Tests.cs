using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class EasyBankXlsxParserTests
{
    private readonly EasyBankXlsxParser parser = new();
    private readonly StatementFile file = Fixtures.Load("easybank", "statement-v1.xlsx");

    [Fact]
    public void CanParse_EasyBankWorkbook_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(file));
        Assert.False(parser.CanParse(Fixtures.Load("revolut", "statement-v1.xlsx")));
        Assert.False(parser.CanParse(Fixtures.Text("some;csv;content")));
    }

    [Fact]
    public void Parse_Fixture_SkipsPendingRows()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(4, result.Rows.Count);
        var skipped = Assert.Single(result.Errors);
        Assert.Contains("vorgemerkt", skipped.Message);
        Assert.Equal(8, skipped.RowNumber);
    }

    [Fact]
    public void Parse_GermanEuroAmounts_AreParsed()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(2345.67m, result.Rows[0].Amount);
        Assert.Equal(-321.09m, result.Rows[1].Amount);
        Assert.Equal(-1.23m, result.Rows[3].Amount);
        Assert.All(result.Rows, row => Assert.Equal("EUR", row.Currency));
    }

    [Fact]
    public void Parse_UsesBookingAndTransactionDates()
    {
        // Act
        var credit = parser.Parse(file).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 26), credit.BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 25), credit.ValueDate);
    }

    [Fact]
    public void Parse_ForeignCurrency_KeepsOriginalAmountInDescription()
    {
        // Act
        var fxRow = parser.Parse(file).Rows[2];

        // Assert
        Assert.Equal(-17.80m, fxRow.Amount);
        Assert.Equal("Sample Cafe", fxRow.Counterparty);
        Assert.Contains("(Original: -25,00 USD)", fxRow.Description);
        Assert.Contains("SAMPLE CAFE_APS SAMPLE CITY US", fxRow.Description);
    }

    [Fact]
    public void Parse_ReferenceNumber_BecomesExternalId()
    {
        // Act
        var row = parser.Parse(file).Rows[1];

        // Assert
        Assert.Equal("100000000000003", row.ExternalId);
    }
}
