using AndreGoepel.FinanceApp.Domain.Imports;
using static AndreGoepel.FinanceApp.Domain.Imports.ImportStatementCommandHandler;

namespace AndreGoepel.FinanceApp.Domain.Tests.Imports;

public sealed class ImportStatementCommandHandlerTests
{
    private static HashedRow Row(string hash, string? externalId = null, int sourceRow = 1) =>
        new(
            new NormalizedTransaction(
                SourceRow: sourceRow,
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

    // The "import anyway" override (#165): the household explicitly wants a
    // specific row imported despite deduping as an existing transaction.

    [Fact]
    public void SplitNewRows_ForcedDatabaseDuplicate_IsImportedAnyway()
    {
        // Act — row 1 dedups against an existing DB hash, but is force-imported.
        var (newRows, duplicates) = SplitNewRows(
            [Row("a", sourceRow: 1)],
            [],
            ["a"],
            forceImportRows: new HashSet<int> { 1 }
        );

        // Assert
        Assert.Single(newRows);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void SplitNewRows_ForcedInFileDuplicate_IsImportedAnyway()
    {
        // Act — two same-file rows share a hash; only the second is forced.
        var (newRows, duplicates) = SplitNewRows(
            [Row("a", sourceRow: 1), Row("a", sourceRow: 2)],
            [],
            [],
            forceImportRows: new HashSet<int> { 2 }
        );

        // Assert — row 1 is the genuine first-seen new row, row 2 is forced despite
        // colliding with it.
        Assert.Equal(2, newRows.Count);
        Assert.Equal([1, 2], newRows.Select(r => r.Row.SourceRow).OrderBy(n => n));
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void SplitNewRows_ForcingOneRow_DoesNotAffectAnUnrelatedDuplicate()
    {
        // Act — row 3 (hash "b") is a genuine, unforced duplicate; forcing row 1
        // must not leak into it.
        var (newRows, duplicates) = SplitNewRows(
            [Row("a", sourceRow: 1), Row("b", sourceRow: 3)],
            [],
            ["a", "b"],
            forceImportRows: new HashSet<int> { 1 }
        );

        // Assert
        Assert.Equal(1, Assert.Single(newRows).Row.SourceRow);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void SplitNewRows_ForcingTheFirstOccurrence_StillLeavesTheSecondADuplicate()
    {
        // Act — row 1 (the genuine first-seen row for hash "a") is forced; row 2
        // shares that hash but is not itself forced.
        var (newRows, duplicates) = SplitNewRows(
            [Row("a", sourceRow: 1), Row("a", sourceRow: 2)],
            [],
            [],
            forceImportRows: new HashSet<int> { 1 }
        );

        // Assert — row 2 is still a real duplicate: forcing row 1 does not exempt
        // a different row that merely shares its key.
        Assert.Equal(1, Assert.Single(newRows).Row.SourceRow);
        Assert.Equal(1, duplicates);
    }
}
