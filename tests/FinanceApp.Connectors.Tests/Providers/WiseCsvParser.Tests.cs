using FinanceApp.Connectors.Parsing;
using FinanceApp.Connectors.Providers;

namespace FinanceApp.Connectors.Tests.Providers;

public class WiseCsvParserTests
{
    private readonly WiseCsvParser parser = new();
    private readonly StatementFile file = Fixtures.Load("wise", "statement-v1.csv");

    [Fact]
    public void CanParse_WiseHeader_ReturnsTrue()
    {
        // Act + Assert
        Assert.True(parser.CanParse(file));
        Assert.False(parser.CanParse(Fixtures.Text("Type,Product,Started Date")));
    }

    [Fact]
    public void Parse_Fixture_ReturnsRowsAndErrors()
    {
        // Act
        var result = parser.Parse(file);

        // Assert
        Assert.Equal(6, result.Rows.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(8, error.RowNumber);
        Assert.Contains("not-a-date", error.Message);
    }

    [Fact]
    public void Parse_CardTransaction_MapsMerchantAndFeeInclusiveAmount()
    {
        // Act
        var row = parser.Parse(file).Rows[0];

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 15), row.BookingDate);
        Assert.Equal(-25.00m, row.Amount);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal("Sample Store CITY", row.Counterparty);
        Assert.Equal("CARD-1000000001", row.ExternalId);
    }

    [Fact]
    public void Parse_OutgoingTransfer_UsesPayeeAsCounterparty()
    {
        // Act
        var row = parser.Parse(file).Rows[1];

        // Assert
        Assert.Equal(-500.00m, row.Amount);
        Assert.Equal("Max Mustermann", row.Counterparty);
    }

    [Fact]
    public void Parse_IncomingDeposit_UsesPayerAsCounterparty()
    {
        // Act
        var row = parser.Parse(file).Rows[2];

        // Assert
        Assert.Equal(1400.00m, row.Amount);
        Assert.Equal("Example Corp OÜ", row.Counterparty);
    }

    [Fact]
    public void Parse_CashbackWithoutParties_HasNoCounterparty()
    {
        // Act
        var row = parser.Parse(file).Rows[5];

        // Assert
        Assert.Equal(1.11m, row.Amount);
        Assert.Null(row.Counterparty);
        Assert.Equal("Cashback", row.Description);
    }
}
