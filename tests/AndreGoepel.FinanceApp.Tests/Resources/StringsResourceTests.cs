using System.Collections;
using System.Globalization;
using System.Resources;
using AndreGoepel.FinanceApp.Resources;
using AndreGoepel.Testing.Bunit;

namespace AndreGoepel.FinanceApp.Tests.Resources;

/// <summary>
/// Pins the EN/DE values of resx-backed keys as each localization batch lands (via
/// <c>[InlineData]</c> rows, following <c>Strings.cs</c>' <c>{Area}.{Purpose}</c> convention), and
/// guards the one gap customer-portal's own equivalent lacks: a key present in only one culture is
/// a silent regression that nothing else would catch.
/// </summary>
public sealed class StringsResourceTests
{
    [Fact]
    public void Resx_EnglishAndGerman_HaveTheSameKeySet()
    {
        // Arrange
        var manager = new ResourceManager(typeof(Strings));
        var en = manager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false
        )!;
        var de = manager.GetResourceSet(
            CultureInfo.GetCultureInfo("de"),
            createIfNotExists: true,
            tryParents: false
        )!;

        var enKeys = en.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var deKeys = de.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        // Act / Assert
        Assert.Equal(enKeys, deKeys);
    }

    [Theory]
    [InlineData("en", "Nav.SectionFinance", "FINANCE")]
    [InlineData("de", "Nav.SectionFinance", "FINANZEN")]
    [InlineData("en", "Nav.SectionFinanceSettings", "FINANCE SETTINGS")]
    [InlineData("de", "Nav.SectionFinanceSettings", "FINANZ-EINSTELLUNGEN")]
    [InlineData("en", "Nav.Transactions", "Transactions")]
    [InlineData("de", "Nav.Transactions", "Umsätze")]
    [InlineData("en", "Nav.Review", "Review")]
    [InlineData("de", "Nav.Review", "Prüfung")]
    [InlineData("en", "Nav.Recurring", "Recurring")]
    [InlineData("de", "Nav.Recurring", "Wiederkehrend")]
    [InlineData("en", "Nav.Planning", "Planning")]
    [InlineData("de", "Nav.Planning", "Planung")]
    [InlineData("en", "Nav.Import", "Import")]
    [InlineData("de", "Nav.Import", "Import")]
    [InlineData("en", "Nav.Sync", "Sync")]
    [InlineData("de", "Nav.Sync", "Sync")]
    [InlineData("en", "Nav.Accounts", "Accounts")]
    [InlineData("de", "Nav.Accounts", "Konten")]
    [InlineData("en", "Nav.Categories", "Categories")]
    [InlineData("de", "Nav.Categories", "Kategorien")]
    [InlineData("en", "Nav.Budgets", "Budgets")]
    [InlineData("de", "Nav.Budgets", "Budgets")]
    [InlineData("en", "Nav.Crypto", "Crypto")]
    [InlineData("de", "Nav.Crypto", "Krypto")]
    [InlineData("en", "Nav.Rules", "Rules")]
    [InlineData("de", "Nav.Rules", "Regeln")]
    [InlineData("en", "Nav.Connections", "Connections")]
    [InlineData("de", "Nav.Connections", "Verbindungen")]
    [InlineData("en", "Nav.ApiKeys", "API Keys")]
    [InlineData("de", "Nav.ApiKeys", "API-Schlüssel")]
    public void GetString_NavKey_ReturnsTheCultureSpecificValue(
        string culture,
        string key,
        string expected
    )
    {
        // Arrange
        using var scope = CultureScope.UiOnly(culture);
        var localizer = FinanceLocalizer.Create();

        // Act
        var value = localizer[key];

        // Assert
        Assert.Equal(expected, value);
        Assert.False(value.ResourceNotFound, $"Key '{key}' is missing for culture '{culture}'.");
    }
}
