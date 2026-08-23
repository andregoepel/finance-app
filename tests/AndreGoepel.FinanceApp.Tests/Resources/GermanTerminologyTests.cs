using System.Collections;
using System.Globalization;
using System.Resources;
using AndreGoepel.FinanceApp.Domain.Resources;
using AndreGoepel.FinanceApp.Resources;

namespace AndreGoepel.FinanceApp.Tests.Resources;

/// <summary>
/// Guards the German vocabulary across <em>both</em> resource sets at once.
/// <para>
/// The per-key pinning tests verify each string in isolation and cannot see that two areas settled
/// on different words for the same thing. That is exactly how the drift this fixes arose: the nav
/// said "Umsätze" while eighteen messages said "Transaktion", and one subtitle managed both in a
/// single sentence. Every one of those keys had a passing pin.
/// </para>
/// <para>
/// So the check is stated negatively — a rejected word, and the word to use instead. That is what
/// survives a new batch: adding a key with the wrong term fails here even though its own pin is
/// perfectly correct. Both resx are scanned together because the split ran across the boundary
/// between them.
/// </para>
/// </summary>
public sealed class GermanTerminologyTests
{
    public static TheoryData<string, string, string> RejectedTerms =>
        new()
        {
            // Chosen over "Umsatz" so the whole app speaks one word; the nav entry moved to match.
            { "Transaktion", "Umsatz", "Umsätze" },
            { "Transaktion", "Umsätze", "Umsätzen" },
            // "Geheimnis" is a secret you keep, not an API credential.
            { "Secret", "Geheimnis", "Geheimnisse" },
            // "Münze" is the physical kind; German crypto usage is "Coin".
            { "Coin", "Münze", "Münzen" },
            // Dashboard.NetWorth and the account messages all say "Nettovermögen".
            { "Nettovermögen", "für das Vermögen", "des Vermögens" },
        };

    [Theory]
    [MemberData(nameof(RejectedTerms))]
    public void GermanValues_UseTheAgreedTerm_NotARejectedSynonym(
        string preferred,
        string rejected,
        string alsoRejected
    )
    {
        // Arrange
        var offenders = GermanEntries()
            .Where(e =>
                e.Value.Contains(rejected, StringComparison.OrdinalIgnoreCase)
                || e.Value.Contains(alsoRejected, StringComparison.OrdinalIgnoreCase)
            )
            .Select(e => $"{e.Key}: {e.Value}")
            .ToArray();

        // Act / Assert
        Assert.True(
            offenders.Length == 0,
            $"Use \"{preferred}\" rather than \"{rejected}\" in German values:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders)
        );
    }

    [Fact]
    public void GermanValues_AreNotIdenticalToEnglish_ForProse()
    {
        // Arrange — a long value that survived untranslated is a copy-paste that no pin catches
        // unless someone thought to write one. Short values legitimately match (Import, Sandbox,
        // Status, API), so only prose is in scope.
        var untranslated = Managers()
            .SelectMany(m =>
                Entries(m, CultureInfo.GetCultureInfo("de"))
                    .Where(de =>
                        de.Value.Length >= 40
                        && m.GetString(de.Key, CultureInfo.InvariantCulture) == de.Value
                    )
                    .Select(de => de.Key)
            )
            .ToArray();

        // Act / Assert
        Assert.Empty(untranslated);
    }

    private static ResourceManager[] Managers() =>
        [new ResourceManager(typeof(Strings)), new ResourceManager(typeof(DomainStrings))];

    private static IEnumerable<KeyValuePair<string, string>> GermanEntries() =>
        Managers().SelectMany(m => Entries(m, CultureInfo.GetCultureInfo("de")));

    private static IEnumerable<KeyValuePair<string, string>> Entries(
        ResourceManager manager,
        CultureInfo culture
    ) =>
        manager
            .GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(e => new KeyValuePair<string, string>((string)e.Key, (string)e.Value!));
}
