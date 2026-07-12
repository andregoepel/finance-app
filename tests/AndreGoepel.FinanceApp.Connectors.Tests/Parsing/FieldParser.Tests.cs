using AndreGoepel.FinanceApp.Connectors.Parsing;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Parsing;

public class FieldParserTests
{
    [Fact]
    public void TryParseAmountWithCurrency_GermanSignedWithCode_ReturnsAmountAndCurrency()
    {
        // Act
        var parsed = FieldParser.TryParseAmountWithCurrency(
            "-25,00 USD",
            out var amount,
            out var currency
        );

        // Assert
        Assert.True(parsed);
        Assert.Equal(-25.00m, amount);
        Assert.Equal("USD", currency);
    }

    [Fact]
    public void TryParseAmountWithCurrency_TypographicMinus_IsNormalized()
    {
        // Act
        var parsed = FieldParser.TryParseAmountWithCurrency(
            "−1.234,56 CHF",
            out var amount,
            out var currency
        );

        // Assert
        Assert.True(parsed);
        Assert.Equal(-1234.56m, amount);
        Assert.Equal("CHF", currency);
    }

    [Fact]
    public void TryParseAmountWithCurrency_EuroSuffix_ReturnsFalse()
    {
        // Act + Assert
        Assert.False(FieldParser.TryParseAmountWithCurrency("25,00 €", out _, out _));
    }

    [Fact]
    public void TryParseAmountWithCurrency_MissingCurrencyCode_ReturnsFalse()
    {
        // Act + Assert
        Assert.False(FieldParser.TryParseAmountWithCurrency("25,00", out _, out _));
    }

    [Fact]
    public void TryParseAmountWithCurrency_NonNumericAmount_ReturnsFalse()
    {
        // Act + Assert
        Assert.False(FieldParser.TryParseAmountWithCurrency("abc USD", out _, out _));
    }
}
