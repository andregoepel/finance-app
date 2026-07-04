using AndreGoepel.FinanceApp.Domain.Imports;
using static AndreGoepel.FinanceApp.Domain.Imports.ImportStatementCommandHandler;

namespace AndreGoepel.FinanceApp.Domain.Tests.Imports;

public class ImportStatementCommandHandlerTests
{
    private static HashedRow Row(string hash) =>
        new(
            new NormalizedTransaction(
                SourceRow: 1,
                BookingDate: new DateOnly(2026, 6, 15),
                ValueDate: null,
                Amount: -1m,
                Currency: "EUR",
                Counterparty: null,
                Description: "d",
                ExternalId: null,
                RawData: "raw"
            ),
            hash
        );

    [Fact]
    public void SplitNewRows_AllNew_ImportsEverything()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("b")], []);

        // Assert
        Assert.Equal(2, newRows.Count);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void SplitNewRows_ExistingHash_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("b")], ["a"]);

        // Assert
        Assert.Equal("b", Assert.Single(newRows).Hash);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void SplitNewRows_RepeatedHashWithinFile_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("a"), Row("a")], []);

        // Assert
        Assert.Single(newRows);
        Assert.Equal(2, duplicates);
    }
}
