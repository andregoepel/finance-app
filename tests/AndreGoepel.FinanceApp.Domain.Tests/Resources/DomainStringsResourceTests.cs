using System.Collections;
using System.Globalization;
using System.Resources;
using AndreGoepel.FinanceApp.Domain.Resources;

namespace AndreGoepel.FinanceApp.Domain.Tests.Resources;

/// <summary>
/// The domain layer's counterpart to the web project's <c>StringsResourceTests</c>: a key present
/// in only one culture is a silent regression — the handler still returns a message, just the
/// English one — so the key sets are compared outright, and a representative message per area is
/// pinned in both languages.
/// <para>
/// Reads through <see cref="ResourceManager"/> with an explicit culture rather than through
/// <c>IStringLocalizer</c> under an ambient one. The handler tests already exercise the localizer
/// path end to end; what is in question here is the resx content itself, and naming the culture per
/// lookup keeps that answer independent of whatever culture the test runner happens to be on.
/// </para>
/// </summary>
public sealed class DomainStringsResourceTests
{
    private static readonly ResourceManager Manager = new(typeof(DomainStrings));

    [Fact]
    public void Resx_EnglishAndGerman_HaveTheSameKeySet()
    {
        // Arrange
        var enKeys = KeysFor(CultureInfo.InvariantCulture);
        var deKeys = KeysFor(CultureInfo.GetCultureInfo("de"));

        // Act / Assert
        Assert.Equal(enKeys, deKeys);
    }

    [Fact]
    public void Resx_EveryKey_IsAnErrorMessage()
    {
        // Arrange / Act — only actionable failure messages belong in the domain layer; anything
        // else is UI wording and belongs in the web project's resx, where it can be reviewed
        // alongside the page that shows it.
        var strays = KeysFor(CultureInfo.InvariantCulture)
            .Where(k => !k.StartsWith("Error.", StringComparison.Ordinal))
            .ToArray();

        // Assert
        Assert.Empty(strays);
    }

    [Theory]
    // Not-found guards — the single most repeated message in the layer.
    [InlineData("en", "Error.AccountNotFound", "Account not found.")]
    [InlineData("de", "Error.AccountNotFound", "Konto nicht gefunden.")]
    // Manual entries (cash) — the guard that keeps hand-entry off imported accounts.
    [InlineData(
        "en",
        "Error.ManualEntriesOnManualAccountsOnly",
        "Transactions can only be entered by hand on manually maintained accounts (cash)."
    )]
    [InlineData(
        "de",
        "Error.ManualEntriesOnManualAccountsOnly",
        "Transaktionen können nur auf manuell geführten Konten (Bargeld) von Hand erfasst werden."
    )]
    // Accounts — a composed message, and one carrying a placeholder.
    [InlineData(
        "en",
        "Error.NonSharedSingleOwner",
        "A non-shared account must have exactly one owner; enable “shared” to add more."
    )]
    [InlineData(
        "de",
        "Error.NonSharedSingleOwner",
        "Ein nicht gemeinsames Konto muss genau einen Inhaber haben; „Gemeinsam“ aktivieren, um weitere hinzuzufügen."
    )]
    [InlineData(
        "en",
        "Error.AccountHasTransactions",
        "This account has {0} transaction(s); deactivate it instead of deleting, so its history is preserved."
    )]
    [InlineData(
        "de",
        "Error.AccountHasTransactions",
        "Dieses Konto hat {0} Transaktion(en); besser deaktivieren statt löschen, damit die Historie erhalten bleibt."
    )]
    // Categories and rules.
    [InlineData(
        "en",
        "Error.CategoryTwoLevelsOnly",
        "Categories support two levels only (group > category)."
    )]
    [InlineData(
        "de",
        "Error.CategoryTwoLevelsOnly",
        "Kategorien unterstützen nur zwei Ebenen (Gruppe > Kategorie)."
    )]
    [InlineData("en", "Error.MinExceedsMax", "Minimum amount must not exceed maximum amount.")]
    [InlineData(
        "de",
        "Error.MinExceedsMax",
        "Der Mindestbetrag darf den Höchstbetrag nicht überschreiten."
    )]
    // Transfers.
    [InlineData(
        "en",
        "Error.AlreadyLinkedAsTransfer",
        "One of the transactions is already linked as a transfer."
    )]
    [InlineData(
        "de",
        "Error.AlreadyLinkedAsTransfer",
        "Eine der Transaktionen ist bereits als Umbuchung verknüpft."
    )]
    // Crypto.
    [InlineData("en", "Error.CryptoAccountsOnly", "Holdings can only be added to crypto accounts.")]
    [InlineData(
        "de",
        "Error.CryptoAccountsOnly",
        "Bestände können nur zu Krypto-Konten hinzugefügt werden."
    )]
    // Credentials.
    [InlineData("en", "Error.SecretMustNotBeEmpty", "The secret must not be empty.")]
    [InlineData("de", "Error.SecretMustNotBeEmpty", "Das Secret darf nicht leer sein.")]
    // Connectors — the parse-time messages a user can act on.
    [InlineData(
        "en",
        "Error.UnrecognizedExportFormat",
        "Unrecognized {0} export format. Supported formats: {1}. The provider may have changed its export — a new parser version is needed."
    )]
    [InlineData(
        "de",
        "Error.UnrecognizedExportFormat",
        "Unbekanntes {0}-Exportformat. Unterstützte Formate: {1}. Möglicherweise hat der Anbieter sein Exportformat geändert — dann wird eine neue Parser-Version benötigt."
    )]
    [InlineData(
        "en",
        "Error.AccountConsentLinkMissing",
        "This account is not linked to any account in the current {0} consent. Re-link it under Settings → Connections."
    )]
    [InlineData(
        "de",
        "Error.AccountConsentLinkMissing",
        "Dieses Konto ist mit keinem Konto der aktuellen {0}-Einwilligung verknüpft. Bitte unter Einstellungen → Verbindungen neu verknüpfen."
    )]
    // Categorization — the one message reaching this resx from outside the Domain project.
    [InlineData(
        "en",
        "Error.NoPendingSuggestions",
        "No pending suggestions found for the selected transactions."
    )]
    [InlineData(
        "de",
        "Error.NoPendingSuggestions",
        "Keine offenen Vorschläge für die ausgewählten Transaktionen gefunden."
    )]
    public void GetString_ErrorKey_ReturnsTheCultureSpecificValue(
        string culture,
        string key,
        string expected
    )
    {
        // Act
        var value = Manager.GetString(key, CultureInfo.GetCultureInfo(culture));

        // Assert
        Assert.Equal(expected, value);
    }

    private static HashSet<string> KeysFor(CultureInfo culture) =>
        Manager
            .GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet();
}
