using AndreGoepel.FinanceApp.Domain.Imports;
using static AndreGoepel.FinanceApp.Domain.Imports.ImportStatementCommandHandler;

namespace AndreGoepel.FinanceApp.Domain.Tests.Imports;

public sealed class ImportStatementCommandHandlerTests
{
    private static HashedRow Row(string hash, string? externalId = null) =>
        new(
            new NormalizedTransaction(
                SourceRow: 1,
                BookingDate: new DateOnly(2026, 6, 15),
                ValueDate: null,
                Amount: -1m,
                Currency: "EUR",
                Counterparty: null,
                Description: "d",
                ExternalId: externalId,
                RawData: "raw"
            ),
            hash
        );

    [Fact]
    public void SplitNewRows_AllNew_ImportsEverything()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("b")], [], []);

        // Assert
        Assert.Equal(2, newRows.Count);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void SplitNewRows_ExistingHash_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("b")], [], ["a"]);

        // Assert
        Assert.Equal("b", Assert.Single(newRows).Hash);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void SplitNewRows_RepeatedHashWithinFile_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows([Row("a"), Row("a"), Row("a")], [], []);

        // Assert
        Assert.Single(newRows);
        Assert.Equal(2, duplicates);
    }

    // A row carrying a provider reference (Enable Banking's EntryReference) dedups on that
    // instead of the hash — same day/amount/description is not enough to call two rows the
    // same transaction once the bank tells them apart (#98).

    [Fact]
    public void SplitNewRows_SameHashDifferentExternalId_BothAreNew()
    {
        // Arrange — two genuinely distinct card transactions on the same day, same amount.
        var rows = new[] { Row("same-hash", "ext-1"), Row("same-hash", "ext-2") };

        // Act
        var (newRows, duplicates) = SplitNewRows(rows, [], []);

        // Assert
        Assert.Equal(2, newRows.Count);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void SplitNewRows_ExistingExternalId_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows(
            [Row("hash", "ext-1"), Row("hash", "ext-2")],
            ["ext-1"],
            []
        );

        // Assert
        Assert.Equal("ext-2", Assert.Single(newRows).Row.ExternalId);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void SplitNewRows_RepeatedExternalIdWithinFile_CountsAsDuplicate()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows(
            [Row("hash", "ext-1"), Row("hash", "ext-1")],
            [],
            []
        );

        // Assert
        Assert.Single(newRows);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void SplitNewRows_RowWithoutExternalId_FallsBackToHashEvenWhenOtherRowsHaveOne()
    {
        // Act
        var (newRows, duplicates) = SplitNewRows(
            [Row("hash", "ext-1"), Row("hash", externalId: null)],
            [],
            []
        );

        // Assert — neither collides with the other: different keys entirely.
        Assert.Equal(2, newRows.Count);
        Assert.Equal(0, duplicates);
    }
}
