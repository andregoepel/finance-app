using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class RevolutXlsxParserTests
{
    private readonly RevolutXlsxParser parser = new();
    private readonly StatementFile file = Fixtures.Load("revolut", "statement-v1.xlsx");

    [Fact]
    public void CanParse_RevolutWorkbook_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(file));
        Assert.False(parser.CanParse(Fixtures.Text("TransferWise ID,Date")));
        Assert.False(parser.CanParse(Fixtures.Load("easybank", "statement-v1.xlsx")));
    }

    [Fact]
    public void Parse_Fixture_ImportsOnlyCompletedRows()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(4, result.Rows.Count);
        var skipped = Assert.Single(result.Errors);
        Assert.Contains("REVERTED", skipped.Message);
        Assert.Equal(5, skipped.RowNumber);
    }

    [Fact]
    public void Parse_UsesCompletedDateAsBookingDateAndStartedAsValueDate()
    {
        // Act
        var cardPayment = parser.Parse(file).Rows[1];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 11), cardPayment.BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 10), cardPayment.ValueDate);
        Assert.Equal(-34.56m, cardPayment.Amount);
        Assert.Equal("EUR", cardPayment.Currency);
    }

    [Fact]
    public void Parse_SubtractsFeeFromAmount()
    {
        // Act
        var feeRow = parser.Parse(file).Rows[3];

        // Assert
        Assert.Equal(-15.25m, feeRow.Amount);
    }

    [Fact]
    public void Parse_CardPayment_UsesDescriptionAsCounterparty()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Null(result.Rows[0].Counterparty); // transfer
        Assert.Equal("Sample Store", result.Rows[1].Counterparty); // card payment
    }
}
