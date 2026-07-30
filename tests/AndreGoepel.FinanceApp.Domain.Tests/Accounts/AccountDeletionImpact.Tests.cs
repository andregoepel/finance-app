using AndreGoepel.FinanceApp.Domain.Accounts;

namespace AndreGoepel.FinanceApp.Domain.Tests.Accounts;

/// <summary>
/// The sentence this produces is the only thing standing between the user and an
/// irreversible delete, so it is pinned by tests rather than eyeballed in the UI.
/// </summary>
public sealed class AccountDeletionImpactTests
{
    [Fact]
    public void IsAccountOnly_NothingAttached_IsTrue()
    {
        // Act / Assert
        Assert.True(AccountDeletionImpact.Nothing.IsAccountOnly);
    }

    [Fact]
    public void IsAccountOnly_AnyAttachedData_IsFalse()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            PlannedItemsDetached = 1,
        };

        // Act / Assert
        Assert.False(impact.IsAccountOnly);
    }

    [Fact]
    public void Describe_NothingAttached_SaysTheAccountHasNoHistory()
    {
        // Act
        var description = AccountDeletionImpact.Nothing.Describe();

        // Assert
        Assert.Equal("Deletes the account. It has no transactions or history.", description);
    }

    [Fact]
    public void Describe_SingleTransaction_UsesSingularWording()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            Transactions = 1,
        };

        // Act
        var description = impact.Describe();

        // Assert
        Assert.Equal("Deletes the account plus 1 transaction.", description);
    }

    [Fact]
    public void Describe_FullCascade_ListsDeletedAndUnlinkedSeparately()
    {
        // Arrange
        var impact = new AccountDeletionImpact(
            Transactions: 42,
            ImportBatches: 3,
            TransfersUnlinked: 2,
            PlannedMatchesCleared: 5,
            ReviewQueueEntries: 7,
            CryptoHoldings: 1,
            PlannedItemsDetached: 4
        );

        // Act
        var description = impact.Describe();

        // Assert
        Assert.Equal(
            "Deletes the account plus 42 transactions, 3 import batches, 1 crypto holding "
                + "and 7 review-queue entries. Unlinks 2 transfers on other accounts, "
                + "5 planned matches and 4 planned items.",
            description
        );
    }

    [Fact]
    public void Describe_OnlyCounterpartsAffected_StillNamesTheAccountDeletion()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            TransfersUnlinked = 1,
        };

        // Act
        var description = impact.Describe();

        // Assert
        Assert.Equal("Deletes the account. Unlinks 1 transfer on another account.", description);
    }
}
