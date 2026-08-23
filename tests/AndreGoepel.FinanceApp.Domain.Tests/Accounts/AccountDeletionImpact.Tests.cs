using AndreGoepel.FinanceApp.Domain.Accounts;

namespace AndreGoepel.FinanceApp.Domain.Tests.Accounts;

/// <summary>
/// The prose form of this impact moved to the UI (AccountDeletionDescription) — building it needs
/// singular/plural selection and list-joining, which are language rules, not domain rules. What
/// stays here is the data contract the sentence is built from.
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
}
