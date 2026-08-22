using System.Collections;
using System.Globalization;
using System.Resources;
using AndreGoepel.FinanceApp.Resources;

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
}
