using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class EasyBankCsvParserTests
{
    private readonly EasyBankCsvParser parser = new();
    private readonly string content = Fixtures.Read("easybank", "statement-v1.csv");

    [Fact]
    public void CanParse_HeaderlessSixColumnShape_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(content));
        Assert.False(parser.CanParse("Type,Product,Started Date,Completed Date"));
    }

    [Fact]
    public void Parse_Fixture_ReturnsRowsAndErrors()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(2, result.Rows.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Contains("zwoelf", error.Message);
    }

    [Fact]
    public void Parse_GermanAmountAndDates_AreParsed()
    {
        // Act
        var row = parser.Parse(content).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 15), row.BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 15), row.ValueDate);
        Assert.Equal(-12.34m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.StartsWith("Bezahlung Karte", row.Description);
    }
}
