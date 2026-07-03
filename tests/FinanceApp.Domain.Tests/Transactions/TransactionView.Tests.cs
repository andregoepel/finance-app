using FinanceApp.Domain.Transactions;

namespace FinanceApp.Domain.Tests.Transactions;

public class TransactionViewTests
{
    private static TransactionView Imported() =>
        TransactionView.Create(
            new TransactionImported(
                TransactionId: Guid.NewGuid(),
                AccountId: Guid.NewGuid(),
                BookingDate: new DateOnly(2026, 6, 15),
                ValueDate: new DateOnly(2026, 6, 16),
                Amount: -23.45m,
                Currency: "EUR",
                AmountEur: -23.45m,
                Counterparty: "REWE",
                Description: "REWE SAGT DANKE",
                ExternalId: "ext-1",
                DedupHash: "hash-1",
                ImportBatchId: Guid.NewGuid(),
                RawData: "raw"
            )
        );

    [Fact]
    public void Create_FromImportedEvent_MapsAllFields()
    {
        // Act
        var view = Imported();

        // Assert
        Assert.Equal(-23.45m, view.Amount);
        Assert.Equal("EUR", view.Currency);
        Assert.Equal("REWE", view.Counterparty);
        Assert.Equal("hash-1", view.DedupHash);
        Assert.Null(view.CategoryId);
        Assert.False(view.IsTransfer);
    }

    [Fact]
    public void Apply_Categorized_SetsCategoryAndSource()
    {
        // Arrange
        var view = Imported();
        var categoryId = Guid.NewGuid();

        // Act
        view.Apply(new TransactionCategorized(categoryId, CategorySource.Manual, null));

        // Assert
        Assert.Equal(categoryId, view.CategoryId);
        Assert.Equal(CategorySource.Manual, view.CategorySource);
    }

    [Fact]
    public void Apply_CategoryCorrected_ReplacesCategoryAsManual()
    {
        // Arrange
        var view = Imported();
        var aiCategory = Guid.NewGuid();
        var corrected = Guid.NewGuid();
        view.Apply(new TransactionCategorized(aiCategory, CategorySource.Ai, 0.9m));

        // Act
        view.Apply(new TransactionCategoryCorrected(aiCategory, corrected));

        // Assert
        Assert.Equal(corrected, view.CategoryId);
        Assert.Equal(CategorySource.Manual, view.CategorySource);
        Assert.Null(view.CategoryConfidence);
    }

    [Fact]
    public void Apply_LinkAndUnlinkTransfer_TogglesTransferState()
    {
        // Arrange
        var view = Imported();
        var counterpart = Guid.NewGuid();

        // Act + Assert
        view.Apply(new TransactionLinkedAsTransfer(counterpart));
        Assert.True(view.IsTransfer);
        Assert.Equal(counterpart, view.TransferCounterpartId);

        view.Apply(new TransactionTransferUnlinked());
        Assert.False(view.IsTransfer);
    }
}
