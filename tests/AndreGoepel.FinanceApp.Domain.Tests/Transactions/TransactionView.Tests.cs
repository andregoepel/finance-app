using AndreGoepel.FinanceApp.Domain.Transactions;

namespace AndreGoepel.FinanceApp.Domain.Tests.Transactions;

public sealed class TransactionViewTests
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
                RawData: "raw",
                OriginalAmount: -27.30m,
                OriginalCurrency: "USD"
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
        Assert.Equal(-27.30m, view.OriginalAmount);
        Assert.Equal("USD", view.OriginalCurrency);
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
    public void Apply_MatchedToPlannedItem_AddsLink()
    {
        // Arrange
        var view = Imported();
        var plannedItemId = Guid.NewGuid();
        var due = new DateOnly(2026, 6, 1);

        // Act
        view.Apply(new TransactionMatchedToPlannedItem(plannedItemId, due));

        // Assert
        Assert.True(view.IsPlanMatched);
        Assert.Equal([new PlannedLink(plannedItemId, due)], view.PlannedLinks);
    }

    [Fact]
    public void Apply_MatchedToPlannedItem_TwoDifferentOccurrences_KeepsBothLinks()
    {
        // Arrange — one transfer covering rent and a car payment: the same
        // transaction satisfies two different planned occurrences.
        var view = Imported();
        var rent = new PlannedLink(Guid.NewGuid(), new DateOnly(2026, 6, 1));
        var car = new PlannedLink(Guid.NewGuid(), new DateOnly(2026, 6, 1));

        // Act
        view.Apply(new TransactionMatchedToPlannedItem(rent.PlannedItemId, rent.DueDate));
        view.Apply(new TransactionMatchedToPlannedItem(car.PlannedItemId, car.DueDate));

        // Assert
        Assert.Equal(2, view.PlannedLinks.Count);
        Assert.Contains(rent, view.PlannedLinks);
        Assert.Contains(car, view.PlannedLinks);
    }

    [Fact]
    public void Apply_MatchedToPlannedItem_SameOccurrenceTwice_IsIdempotent()
    {
        // Arrange
        var view = Imported();
        var plannedItemId = Guid.NewGuid();
        var due = new DateOnly(2026, 6, 1);
        view.Apply(new TransactionMatchedToPlannedItem(plannedItemId, due));

        // Act — a re-match (e.g. a repeated "Use" click) must not duplicate the link.
        view.Apply(new TransactionMatchedToPlannedItem(plannedItemId, due));

        // Assert
        Assert.Single(view.PlannedLinks);
    }

    [Fact]
    public void Apply_PlannedMatchCleared_RemovesOnlyTheNamedLink()
    {
        // Arrange
        var view = Imported();
        var rent = new PlannedLink(Guid.NewGuid(), new DateOnly(2026, 6, 1));
        var car = new PlannedLink(Guid.NewGuid(), new DateOnly(2026, 6, 1));
        view.Apply(new TransactionMatchedToPlannedItem(rent.PlannedItemId, rent.DueDate));
        view.Apply(new TransactionMatchedToPlannedItem(car.PlannedItemId, car.DueDate));

        // Act
        view.Apply(new TransactionPlannedMatchCleared(rent.PlannedItemId, rent.DueDate));

        // Assert
        Assert.True(view.IsPlanMatched);
        Assert.Equal([car], view.PlannedLinks);
    }

    [Fact]
    public void Apply_PlannedMatchCleared_WithoutOccurrence_ClearsEverything()
    {
        // Arrange — the pre-multi-match event shape (no payload); still must work
        // for history recorded before this feature existed.
        var view = Imported();
        view.Apply(new TransactionMatchedToPlannedItem(Guid.NewGuid(), new DateOnly(2026, 6, 1)));

        // Act
        view.Apply(new TransactionPlannedMatchCleared());

        // Assert
        Assert.False(view.IsPlanMatched);
        Assert.Empty(view.PlannedLinks);
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
