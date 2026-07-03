using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class DkbCsvParserTests
{
    private readonly DkbCsvParser parser = new();
    private readonly string content = Fixtures.Read("dkb", "statement-v1.csv");

    [Fact]
    public void CanParse_DkbHeader_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(content));
        Assert.False(parser.CanParse("IBAN;Text;Datum"));
    }

    [Fact]
    public void Parse_Fixture_ImportsOnlyBookedRows()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(2, result.Rows.Count);
        var skipped = Assert.Single(result.Errors);
        Assert.Contains("Vorgemerkt", skipped.Message);
    }

    [Fact]
    public void Parse_GermanAmountAndShortDate_AreParsed()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 15), result.Rows[0].BookingDate);
        Assert.Equal(-54.32m, result.Rows[0].Amount);
        Assert.Equal(2500.00m, result.Rows[1].Amount);
        Assert.Equal("EUR", result.Rows[0].Currency);
    }

    [Fact]
    public void Parse_Counterparty_DependsOnAmountSign()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal("REWE Markt GmbH", result.Rows[0].Counterparty);
        Assert.Equal("ACME GmbH", result.Rows[1].Counterparty);
    }
}
