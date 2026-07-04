using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class DkbCsvParserTests
{
    private readonly DkbCsvParser parser = new();
    private readonly StatementFile file = Fixtures.Load("dkb", "statement-v1.csv");

    [Fact]
    public void CanParse_DkbHeader_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(file));
        Assert.False(parser.CanParse(Fixtures.Text("IBAN;Text;Datum")));
    }

    [Fact]
    public void Parse_Fixture_ImportsOnlyBookedReadableRows()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("Vorgemerkt"));
        Assert.Contains(result.Errors, e => e.Message.Contains("zwanzig"));
    }

    [Fact]
    public void Parse_GermanAmounts_IncludingIntegerShorthand_AreParsed()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(75.00m, result.Rows[0].Amount); // "75" without decimals
        Assert.Equal(-12.34m, result.Rows[1].Amount);
        Assert.Equal(-54.32m, result.Rows[2].Amount);
        Assert.Equal(2500.00m, result.Rows[3].Amount); // "2.500,00"
        Assert.All(result.Rows, row => Assert.Equal("EUR", row.Currency));
    }

    [Fact]
    public void Parse_ShortDates_AreParsed()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 29), result.Rows[0].BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 29), result.Rows[0].ValueDate);
    }

    [Fact]
    public void Parse_Counterparty_DependsOnAmountSign()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal("Sample Store GmbH", result.Rows[2].Counterparty); // debit → payee
        Assert.Equal("Example Corp GmbH", result.Rows[3].Counterparty); // credit → payer
    }
}
