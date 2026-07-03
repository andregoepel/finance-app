using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class CryptoComCsvParserTests
{
    private readonly CryptoComCsvParser parser = new();
    private readonly string content = Fixtures.Read("cryptocom", "statement-v1.csv");

    [Fact]
    public void CanParse_CryptoComHeader_ReturnsTrue()
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
        Assert.Contains("not-a-number", error.Message);
    }

    [Fact]
    public void Parse_UsesNativeAmountAndKeepsAssetInDescription()
    {
        // Act
        var row = parser.Parse(content).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 20), row.BookingDate);
        Assert.Equal(-312.45m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal("Buy BTC (0.00500000 BTC)", row.Description);
        Assert.Null(row.ExternalId);
    }

    [Fact]
    public void Parse_TransactionHash_BecomesExternalId()
    {
        // Act
        var row = parser.Parse(content).Rows[1];

        // Assert
        Assert.Equal("abcdef1234567890", row.ExternalId);
    }
}
