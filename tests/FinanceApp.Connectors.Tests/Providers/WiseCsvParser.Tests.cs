using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class WiseCsvParserTests
{
    private readonly WiseCsvParser parser = new();
    private readonly string content = Fixtures.Read("wise", "statement-v1.csv");

    [Fact]
    public void CanParse_WiseHeader_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(content));
        Assert.False(parser.CanParse("Type,Product,Started Date"));
    }

    [Fact]
    public void Parse_Fixture_ReturnsRowsAndErrors()
    {
        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(2, result.Rows.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(4, error.RowNumber);
        Assert.Contains("not-a-date", error.Message);
    }

    [Fact]
    public void Parse_CardPayment_MapsMerchantAndAmount()
    {
        // Act
        var row = parser.Parse(content).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 15), row.BookingDate);
        Assert.Equal(-23.45m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal("REWE Markt GmbH", row.Counterparty);
        Assert.Equal("BALANCE-100001", row.ExternalId);
    }

    [Fact]
    public void Parse_IncomingPayment_UsesPayerAsCounterparty()
    {
        // Act
        var row = parser.Parse(content).Rows[1];

        // Assert
        Assert.Equal(1500.00m, row.Amount);
        Assert.Equal("ACME GmbH", row.Counterparty);
    }
}
