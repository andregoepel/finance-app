using AndreGoepel.FinanceApp.Components.Settings;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.Testing.Bunit;

namespace AndreGoepel.FinanceApp.Tests.Components.Settings;

/// <summary>
/// The sentence this produces is the only thing standing between the user and an irreversible
/// delete, so it is pinned by tests rather than eyeballed in the UI. Moved here from the Domain
/// test project along with the logic itself: picking singular or plural and joining a list with
/// "and" are language rules, so they belong where the localizer is.
/// <para>
/// The English assertions are carried over unchanged from the former
/// <c>AccountDeletionImpact.Describe()</c> tests — the rendered English must not have drifted.
/// </para>
/// </summary>
public sealed class AccountDeletionDescriptionTests
{
    private static string Build(AccountDeletionImpact impact, string culture)
    {
        using var scope = CultureScope.UiOnly(culture);
        return AccountDeletionDescription.Build(impact, FinanceLocalizer.Create());
    }

    [Fact]
    public void Build_NothingAttached_SaysTheAccountHasNoHistory()
    {
        // Act
        var en = Build(AccountDeletionImpact.Nothing, "en");
        var de = Build(AccountDeletionImpact.Nothing, "de");

        // Assert
        Assert.Equal("Deletes the account. It has no transactions or history.", en);
        Assert.Equal("Löscht das Konto. Es hat keine Transaktionen oder Historie.", de);
    }

    [Fact]
    public void Build_SingleTransaction_UsesSingularWording()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            Transactions = 1,
        };

        // Act
        var en = Build(impact, "en");
        var de = Build(impact, "de");

        // Assert — real singular selection, not the "(s)" hedge used where the count is unknown.
        Assert.Equal("Deletes the account plus 1 transaction.", en);
        Assert.Equal("Löscht das Konto und zusätzlich: 1 Transaktion.", de);
    }

    [Fact]
    public void Build_TwoTransactions_UsesPluralWording()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            Transactions = 2,
        };

        // Act / Assert
        Assert.Equal("Deletes the account plus 2 transactions.", Build(impact, "en"));
        Assert.Equal("Löscht das Konto und zusätzlich: 2 Transaktionen.", Build(impact, "de"));
    }

    [Fact]
    public void Build_FullCascade_ListsDeletedAndUnlinkedSeparately()
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
        var en = Build(impact, "en");
        var de = Build(impact, "de");

        // Assert — the English wording is unchanged from before the move.
        Assert.Equal(
            "Deletes the account plus 42 transactions, 3 import batches, 1 crypto holding "
                + "and 7 review-queue entries. Unlinks 2 transfers on other accounts, "
                + "5 planned matches and 4 planned items.",
            en
        );
        Assert.Equal(
            "Löscht das Konto und zusätzlich: 42 Transaktionen, 3 Import-Vorgänge, "
                + "1 Krypto-Bestand und 7 Prüf-Einträge. Hebt die Verknüpfung auf für: "
                + "2 Umbuchungen auf anderen Konten, 5 geplante Zuordnungen und 4 geplante Posten.",
            de
        );
    }

    [Fact]
    public void Build_OnlyCounterpartsAffected_StillNamesTheAccountDeletion()
    {
        // Arrange
        var impact = AccountDeletionImpact.Nothing with
        {
            TransfersUnlinked = 1,
        };

        // Act
        var en = Build(impact, "en");
        var de = Build(impact, "de");

        // Assert
        Assert.Equal("Deletes the account. Unlinks 1 transfer on another account.", en);
        Assert.Equal(
            "Löscht das Konto. Hebt die Verknüpfung auf für: 1 Umbuchung auf einem anderen Konto.",
            de
        );
    }
}
