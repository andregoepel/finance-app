using FinanceApp.Connectors.Parsing;
using FinanceApp.Connectors.Providers;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Tests.Parsing;

public class StatementParserRegistryTests
{
    private static StatementParserRegistry BuildRegistry() =>
        new([new WiseCsvParser(), new RevolutCsvParser()]);

    [Fact]
    public void Parse_MatchingFormat_UsesTheRightParser()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(ProviderKind.Wise, Fixtures.Read("wise", "statement-v1.csv"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("wise-csv-v1", result.Value!.ParserId);
    }

    [Fact]
    public void Parse_UnknownFormat_FailsLoudlyWithSupportedFormats()
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var result = registry.Parse(ProviderKind.Wise, "some;unknown;content");

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
        var result = registry.Parse(ProviderKind.Dkb, "anything");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("No statement parser", result.Error);
    }
}
