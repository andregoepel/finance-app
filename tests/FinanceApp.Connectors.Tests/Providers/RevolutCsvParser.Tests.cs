using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class RevolutCsvParserTests
{
    private readonly RevolutCsvParser parser = new();
    private readonly string content = Fixtures.Read("revolut", "statement-v1.csv");

    [Fact]
    public void CanParse_RevolutHeader_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(content));
        Assert.False(parser.CanParse("TransferWise ID,Date,Amount"));
    }

    [Fact]
    public void Parse_Fixture_ImportsOnlyCompletedRows()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(3, result.Rows.Count);
        var skipped = Assert.Single(result.Errors);
        Assert.Contains("PENDING", skipped.Message);
        Assert.Equal(4, skipped.RowNumber);
    }

    [Fact]
    public void Parse_UsesCompletedDateAsBookingDate()
    {
        // Act
        var row = parser.Parse(content).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 11), row.BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 10), row.ValueDate);
    }

    [Fact]
    public void Parse_SubtractsFeeFromAmount()
    {
        // Act
        var transfer = parser.Parse(content).Rows[2];

        // Assert
        Assert.Equal(-200.50m, transfer.Amount);
    }

    [Fact]
    public void Parse_CardPayment_UsesDescriptionAsCounterparty()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal("Lidl", result.Rows[0].Counterparty);
        Assert.Null(result.Rows[1].Counterparty);
    }
}
