using AndreGoepel.FinanceApp.Connectors.Parsing;
using AndreGoepel.FinanceApp.Connectors.Providers;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Parsing;

public sealed class StatementParserRegistryTests
{
    private static StatementParserRegistry BuildRegistry() =>
        new([new WiseCsvParser(), new RevolutXlsxParser()], DomainLocalizer.Instance);

    [Fact]
    public void Parse_MatchingFormat_UsesTheRightParser()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(ProviderKind.Wise, Fixtures.Load("wise", "statement-v1.csv"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("wise-csv-v1", result.Value!.ParserId);
    }

    [Fact]
    public void Parse_XlsxFormat_UsesTheXlsxParser()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(
            ProviderKind.Revolut,
            Fixtures.Load("revolut", "statement-v1.xlsx")
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("revolut-xlsx-v1", result.Value!.ParserId);
    }

    [Fact]
    public void Parse_UnknownFormat_FailsLoudlyWithSupportedFormats()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(ProviderKind.Wise, Fixtures.Text("some;unknown;content"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("wise-csv-v1", result.Error);
        Assert.Contains("Unrecognized", result.Error);
    }

    [Fact]
    public void Parse_ProviderWithoutParser_Fails()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(ProviderKind.Dkb, Fixtures.Text("anything"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("No statement parser", result.Error);
    }
}
