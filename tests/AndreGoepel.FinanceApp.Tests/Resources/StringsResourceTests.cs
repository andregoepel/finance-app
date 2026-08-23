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
    [InlineData("de", "Nav.Review", "Zu prüfen")]
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

    [Theory]
    [InlineData("en", "Common.Save", "Save")]
    [InlineData("de", "Common.Save", "Speichern")]
    [InlineData("en", "Common.Saved", "Saved")]
    [InlineData("de", "Common.Saved", "Gespeichert")]
    [InlineData("en", "Common.SavingFailedTitle", "Saving failed")]
    [InlineData("de", "Common.SavingFailedTitle", "Speichern fehlgeschlagen")]
    [InlineData("en", "Common.Delete", "Delete")]
    [InlineData("de", "Common.Delete", "Löschen")]
    [InlineData("en", "ApiKeys.ClaudeSectionTitle", "Claude API key")]
    [InlineData("de", "ApiKeys.ClaudeSectionTitle", "Claude-API-Schlüssel")]
    [InlineData(
        "en",
        "ApiKeys.SecretsIntro",
        "Secrets are stored encrypted in the database (DataProtection) and never shown again after saving. Wise and Enable Banking credentials live under"
    )]
    [InlineData(
        "de",
        "ApiKeys.SecretsIntro",
        "Geheimnisse werden verschlüsselt in der Datenbank gespeichert (DataProtection) und nach dem Speichern nie wieder angezeigt. Wise- und Enable-Banking-Zugangsdaten befinden sich unter"
    )]
    [InlineData(
        "en",
        "ApiKeys.ClaudeDescription",
        "Used for AI categorization of imported transactions. Without a key, uncategorized transactions simply stay in the review queue."
    )]
    [InlineData(
        "de",
        "ApiKeys.ClaudeDescription",
        "Wird für die KI-Kategorisierung importierter Transaktionen verwendet. Ohne Schlüssel bleiben unkategorisierte Transaktionen einfach zur Prüfung liegen."
    )]
    [InlineData("en", "ApiKeys.NotConfigured", "not configured")]
    [InlineData("de", "ApiKeys.NotConfigured", "nicht konfiguriert")]
    [InlineData("en", "ApiKeys.RotateKeyPlaceholder", "Enter new key to rotate")]
    [InlineData(
        "de",
        "ApiKeys.RotateKeyPlaceholder",
        "Neuen Schlüssel eingeben, um ihn zu ersetzen"
    )]
    [InlineData("en", "ApiKeys.ClaudeKeySavedMessage", "Claude API key stored encrypted.")]
    [InlineData(
        "de",
        "ApiKeys.ClaudeKeySavedMessage",
        "Claude-API-Schlüssel verschlüsselt gespeichert."
    )]
    [InlineData("en", "Categories.NewSubcategoryPlaceholder", "New subcategory")]
    [InlineData("de", "Categories.NewSubcategoryPlaceholder", "Neue Unterkategorie")]
    [InlineData("en", "Categories.NewGroupPlaceholder", "New group")]
    [InlineData("de", "Categories.NewGroupPlaceholder", "Neue Gruppe")]
    [InlineData("en", "Categories.AddGroup", "Add group")]
    [InlineData("de", "Categories.AddGroup", "Gruppe hinzufügen")]
    [InlineData("en", "Categories.RenameFailedTitle", "Rename failed")]
    [InlineData("de", "Categories.RenameFailedTitle", "Umbenennen fehlgeschlagen")]
    [InlineData("en", "Categories.CreatingFailedTitle", "Creating failed")]
    [InlineData("de", "Categories.CreatingFailedTitle", "Erstellen fehlgeschlagen")]
    [InlineData("en", "Categories.DeleteRefusedTitle", "Delete refused")]
    [InlineData("de", "Categories.DeleteRefusedTitle", "Löschen abgelehnt")]
    public void GetString_B2Key_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_SettingsBreadcrumb_FormatsThePageNameIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Common.SettingsBreadcrumb", "Categories"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Common.SettingsBreadcrumb", "Kategorien"];
        }

        // Assert
        Assert.Equal("Settings / Categories", en);
        Assert.Equal("Einstellungen / Kategorien", de);
    }

    [Fact]
    public void GetString_ApiKeysConfiguredOn_FormatsTheDateIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["ApiKeys.ConfiguredOn", "22.08.2026"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["ApiKeys.ConfiguredOn", "22.08.2026"];
        }

        // Assert
        Assert.Equal("configured 22.08.2026", en);
        Assert.Equal("konfiguriert am 22.08.2026", de);
    }

    [Theory]
    [InlineData("en", "Common.Loading", "Loading…")]
    [InlineData("de", "Common.Loading", "Wird geladen…")]
    [InlineData("en", "Common.Counterparty", "Counterparty")]
    [InlineData("de", "Common.Counterparty", "Zahlungspartner")]
    [InlineData("en", "Common.Account", "Account")]
    [InlineData("de", "Common.Account", "Konto")]
    [InlineData("en", "Common.Provider", "Provider")]
    [InlineData("de", "Common.Provider", "Anbieter")]
    [InlineData("en", "Common.Enabled", "Enabled")]
    [InlineData("de", "Common.Enabled", "Aktiviert")]
    [InlineData("en", "Common.Disabled", "Disabled")]
    [InlineData("de", "Common.Disabled", "Deaktiviert")]
    [InlineData(
        "en",
        "Recurring.Subtitle",
        "Detected subscriptions and other regular payments or income, based on repeating counterparties with a consistent interval and amount."
    )]
    [InlineData(
        "de",
        "Recurring.Subtitle",
        "Erkannte Abonnements und andere regelmäßige Zahlungen oder Einnahmen, basierend auf wiederkehrenden Zahlungen an denselben Zahlungspartner mit gleichbleibendem Intervall und Betrag."
    )]
    [InlineData("en", "Recurring.EmptyTitle", "No recurring payments detected yet")]
    [InlineData("de", "Recurring.EmptyTitle", "Noch keine wiederkehrenden Zahlungen erkannt")]
    [InlineData(
        "en",
        "Recurring.EmptyText",
        "This needs a few months of transactions before patterns emerge."
    )]
    [InlineData(
        "de",
        "Recurring.EmptyText",
        "Dafür braucht es erst einige Monate an Transaktionen, bevor sich Muster abzeichnen."
    )]
    [InlineData("en", "Recurring.ColInterval", "Interval")]
    [InlineData("de", "Recurring.ColInterval", "Intervall")]
    [InlineData("en", "Recurring.ColTypicalAmount", "Typical amount")]
    [InlineData("de", "Recurring.ColTypicalAmount", "Typ. Betrag")]
    [InlineData("en", "Recurring.ColSeen", "Seen")]
    [InlineData("de", "Recurring.ColSeen", "Anzahl")]
    [InlineData("en", "Recurring.ColLast", "Last")]
    [InlineData("de", "Recurring.ColLast", "Zuletzt")]
    [InlineData("en", "Recurring.ColNextExpected", "Next expected")]
    [InlineData("de", "Recurring.ColNextExpected", "Nächster Termin")]
    [InlineData("en", "Recurring.AddAsPlanned", "Add as planned")]
    [InlineData("de", "Recurring.AddAsPlanned", "Zur Planung hinzufügen")]
    [InlineData("en", "Recurring.CouldNotAddTitle", "Could not add")]
    [InlineData("de", "Recurring.CouldNotAddTitle", "Hinzufügen fehlgeschlagen")]
    [InlineData("en", "Recurring.AddedToPlanningTitle", "Added to planning")]
    [InlineData("de", "Recurring.AddedToPlanningTitle", "Zur Planung hinzugefügt")]
    [InlineData("en", "Enum.RecurrenceInterval.Weekly", "Weekly")]
    [InlineData("de", "Enum.RecurrenceInterval.Weekly", "Wöchentlich")]
    [InlineData("en", "Enum.RecurrenceInterval.Biweekly", "Biweekly")]
    [InlineData("de", "Enum.RecurrenceInterval.Biweekly", "Zweiwöchentlich")]
    [InlineData("en", "Enum.RecurrenceInterval.Monthly", "Monthly")]
    [InlineData("de", "Enum.RecurrenceInterval.Monthly", "Monatlich")]
    [InlineData("en", "Enum.RecurrenceInterval.Quarterly", "Quarterly")]
    [InlineData("de", "Enum.RecurrenceInterval.Quarterly", "Vierteljährlich")]
    [InlineData("en", "Enum.RecurrenceInterval.Yearly", "Yearly")]
    [InlineData("de", "Enum.RecurrenceInterval.Yearly", "Jährlich")]
    [InlineData("en", "Sync.SyncAllNow", "Sync all now")]
    [InlineData("de", "Sync.SyncAllNow", "Jetzt alle synchronisieren")]
    [InlineData("en", "Sync.SyncingBusyText", "Syncing...")]
    [InlineData("de", "Sync.SyncingBusyText", "Wird synchronisiert…")]
    [InlineData(
        "en",
        "Sync.Intro",
        "API-backed accounts sync automatically on the schedule below; trigger a manual sync any time here. Configure credentials and bank consents under"
    )]
    [InlineData(
        "de",
        "Sync.Intro",
        "API-basierte Konten werden automatisch nach dem unten stehenden Zeitplan synchronisiert; hier kann jederzeit eine manuelle Synchronisierung ausgelöst werden. Zugangsdaten und Bank-Einwilligungen befinden sich unter"
    )]
    [InlineData("en", "Sync.SettingsConnectionsLink", "Settings → Connections")]
    [InlineData("de", "Sync.SettingsConnectionsLink", "Einstellungen → Verbindungen")]
    [InlineData("en", "Sync.ConsentAttentionNeeded", "Consent attention needed:")]
    [InlineData("de", "Sync.ConsentAttentionNeeded", "Bank-Einwilligungen prüfen:")]
    [InlineData("en", "Sync.ConsentExpired", "expired")]
    [InlineData("de", "Sync.ConsentExpired", "abgelaufen")]
    [InlineData("en", "Sync.AutomaticScheduleTitle", "Automatic schedule")]
    [InlineData("de", "Sync.AutomaticScheduleTitle", "Automatischer Zeitplan")]
    [InlineData("en", "Sync.CronLabel", "Cron expression (Quartz)")]
    [InlineData("de", "Sync.CronLabel", "Cron-Ausdruck (Quartz)")]
    [InlineData("en", "Sync.SaveScheduleButton", "Save schedule")]
    [InlineData("de", "Sync.SaveScheduleButton", "Zeitplan speichern")]
    [InlineData("en", "Sync.PresetsLabel", "Presets:")]
    [InlineData("de", "Sync.PresetsLabel", "Voreinstellungen:")]
    [InlineData("en", "Sync.PresetDaily3am", "Daily 03:00")]
    [InlineData("de", "Sync.PresetDaily3am", "Täglich 03:00")]
    [InlineData("en", "Sync.PresetDaily6am", "Daily 06:00")]
    [InlineData("de", "Sync.PresetDaily6am", "Täglich 06:00")]
    [InlineData("en", "Sync.PresetEvery6Hours", "Every 6 hours")]
    [InlineData("de", "Sync.PresetEvery6Hours", "Alle 6 Stunden")]
    [InlineData("en", "Sync.PresetHourly", "Hourly")]
    [InlineData("de", "Sync.PresetHourly", "Stündlich")]
    [InlineData("en", "Sync.InvalidCronExpression", "Not a valid cron expression.")]
    [InlineData("de", "Sync.InvalidCronExpression", "Kein gültiger Cron-Ausdruck.")]
    [InlineData("en", "Sync.EmptyTitle", "No API-backed accounts yet")]
    [InlineData("de", "Sync.EmptyTitle", "Noch keine API-basierten Konten")]
    [InlineData("en", "Sync.ColLastSync", "Last sync")]
    [InlineData("de", "Sync.ColLastSync", "Letzte Synchronisierung")]
    [InlineData("en", "Sync.NeverSynced", "never")]
    [InlineData("de", "Sync.NeverSynced", "nie")]
    [InlineData("en", "Sync.SyncNowButton", "Sync now")]
    [InlineData("de", "Sync.SyncNowButton", "Jetzt synchronisieren")]
    [InlineData("en", "Sync.InvalidScheduleTitle", "Invalid schedule")]
    [InlineData("de", "Sync.InvalidScheduleTitle", "Ungültiger Zeitplan")]
    [InlineData("en", "Sync.ScheduleSavedTitle", "Schedule saved")]
    [InlineData("de", "Sync.ScheduleSavedTitle", "Zeitplan gespeichert")]
    [InlineData("en", "Sync.RescheduledMessage", "Automatic sync rescheduled.")]
    [InlineData("de", "Sync.RescheduledMessage", "Automatische Synchronisierung neu geplant.")]
    [InlineData("en", "Sync.DisabledMessage", "Automatic sync disabled.")]
    [InlineData("de", "Sync.DisabledMessage", "Automatische Synchronisierung deaktiviert.")]
    public void GetString_B3Key_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_RecurringAddedToPlanningMessage_FormatsTheCounterpartyIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Recurring.AddedToPlanningMessage", "Netflix"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Recurring.AddedToPlanningMessage", "Netflix"];
        }

        // Assert
        Assert.Equal("“Netflix” — adjust it on the Planning page.", en);
        Assert.Equal("„Netflix“ — auf der Planungsseite anpassen.", de);
    }

    [Fact]
    public void GetString_SyncConsentExpiresOn_FormatsTheDateIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.ConsentExpiresOn", "22.08.2026"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.ConsentExpiresOn", "22.08.2026"];
        }

        // Assert
        Assert.Equal("expires 22.08.2026", en);
        Assert.Equal("läuft ab am 22.08.2026", de);
    }

    [Fact]
    public void GetString_SyncOffPrompt_FormatsTheButtonLabelIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.OffPrompt", "Sync all now"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.OffPrompt", "Jetzt alle synchronisieren"];
        }

        // Assert
        Assert.Equal("Automatic sync is off — use “Sync all now” to run manually.", en);
        Assert.Equal(
            "Automatische Synchronisierung ist deaktiviert — „Jetzt alle synchronisieren“ nutzen, um sie manuell auszulösen.",
            de
        );
    }

    [Fact]
    public void GetString_SyncNextRun_FormatsTheDateTimeIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.NextRun", "Monday, 24.08.2026 03:00"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.NextRun", "Montag, 24.08.2026 03:00"];
        }

        // Assert
        Assert.Equal("Next run: Monday, 24.08.2026 03:00", en);
        Assert.Equal("Nächster Lauf: Montag, 24.08.2026 03:00", de);
    }

    [Fact]
    public void GetString_SyncEmptyText_FormatsTheNavLabelIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.EmptyText", "Accounts"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.EmptyText", "Konten"];
        }

        // Assert
        Assert.Equal("Set an account's sync method to API under Accounts to sync it here.", en);
        Assert.Equal(
            "Die Synchronisierungsmethode eines Kontos unter „Konten“ auf API setzen, um es hier zu synchronisieren.",
            de
        );
    }

    [Fact]
    public void GetString_SyncAccountNotSyncedTitle_FormatsTheAccountNameIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.AccountNotSyncedTitle", "Checking"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.AccountNotSyncedTitle", "Checking"];
        }

        // Assert
        Assert.Equal("Checking not synced", en);
        Assert.Equal("Checking nicht synchronisiert", de);
    }

    [Fact]
    public void GetString_SyncAccountSyncedTitle_FormatsTheAccountNameIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.AccountSyncedTitle", "Checking"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.AccountSyncedTitle", "Checking"];
        }

        // Assert
        Assert.Equal("Checking synced", en);
        Assert.Equal("Checking synchronisiert", de);
    }

    [Fact]
    public void GetString_SyncSyncedMessage_FormatsTheCountsIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Sync.SyncedMessage", 3, 1];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Sync.SyncedMessage", 3, 1];
        }

        // Assert
        Assert.Equal("3 imported, 1 duplicates.", en);
        Assert.Equal("3 importiert, 1 Duplikate.", de);
    }

    [Theory]
    [InlineData("en", "Common.Date", "Date")]
    [InlineData("de", "Common.Date", "Datum")]
    [InlineData("en", "Common.Description", "Description")]
    [InlineData("de", "Common.Description", "Beschreibung")]
    [InlineData("en", "Common.Amount", "Amount")]
    [InlineData("de", "Common.Amount", "Betrag")]
    [InlineData("en", "Common.Status", "Status")]
    [InlineData("de", "Common.Status", "Status")]
    [InlineData("en", "Common.FailedTitle", "Failed")]
    [InlineData("de", "Common.FailedTitle", "Fehlgeschlagen")]
    [InlineData("en", "Common.DoneTitle", "Done")]
    [InlineData("de", "Common.DoneTitle", "Fertig")]
    [InlineData("en", "Common.ChooseCategoryPlaceholder", "Choose category")]
    [InlineData("de", "Common.ChooseCategoryPlaceholder", "Kategorie wählen")]
    [InlineData("en", "Review.PageTitle", "Review queue")]
    [InlineData("de", "Review.PageTitle", "Zu prüfen")]
    [InlineData("en", "Review.AcceptSuggestions", "Accept suggestions")]
    [InlineData("de", "Review.AcceptSuggestions", "Vorschläge übernehmen")]
    [InlineData("en", "Review.DismissSuggestions", "Dismiss suggestions")]
    [InlineData("de", "Review.DismissSuggestions", "Vorschläge verwerfen")]
    [InlineData("en", "Review.SetCategoryLabel", "Set category for selection")]
    [InlineData("de", "Review.SetCategoryLabel", "Kategorie für die Auswahl festlegen")]
    [InlineData("en", "Review.ApplyToSelection", "Apply to selection")]
    [InlineData("de", "Review.ApplyToSelection", "Auf Auswahl anwenden")]
    [InlineData("en", "Review.ColAiSuggestion", "AI suggestion")]
    [InlineData("de", "Review.ColAiSuggestion", "KI-Vorschlag")]
    [InlineData("en", "Review.AcceptSuggestionTooltip", "Accept suggestion")]
    [InlineData("de", "Review.AcceptSuggestionTooltip", "Vorschlag übernehmen")]
    [InlineData("en", "Review.SuggestionAcceptedMessage", "Suggestion accepted.")]
    [InlineData("de", "Review.SuggestionAcceptedMessage", "Vorschlag übernommen.")]
    [InlineData("en", "Review.SuggestionsAcceptedMessage", "Suggestions accepted.")]
    [InlineData("de", "Review.SuggestionsAcceptedMessage", "Vorschläge übernommen.")]
    [InlineData("en", "Review.SuggestionsDismissedMessage", "Suggestions dismissed.")]
    [InlineData("de", "Review.SuggestionsDismissedMessage", "Vorschläge verworfen.")]
    public void GetString_B4Key_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_ReviewSubtitle_PinsBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Review.Subtitle"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Review.Subtitle"];
        }

        // Assert
        Assert.Equal(
            "Uncategorized transactions and low-confidence AI suggestions. High-confidence suggestions are applied automatically and flagged \"Ai\" in the transactions grid.",
            en
        );
        Assert.Equal(
            "Unkategorisierte Transaktionen und KI-Vorschläge mit geringer Konfidenz. Vorschläge mit hoher Konfidenz werden automatisch angewendet und in der Umsatzübersicht mit „KI“ markiert.",
            de
        );
    }

    [Theory]
    [InlineData("en", "Import.SelectAccountPlaceholder", "Select account")]
    [InlineData("de", "Import.SelectAccountPlaceholder", "Konto auswählen")]
    [InlineData("en", "Import.ColRow", "Row")]
    [InlineData("de", "Import.ColRow", "Zeile")]
    [InlineData("en", "Import.DuplicateBadge", "Duplicate")]
    [InlineData("de", "Import.DuplicateBadge", "Duplikat")]
    [InlineData("en", "Import.NewBadge", "New")]
    [InlineData("de", "Import.NewBadge", "Neu")]
    [InlineData("en", "Import.ProblemRowsHeading", "Problem rows (not imported)")]
    [InlineData("de", "Import.ProblemRowsHeading", "Fehlerhafte Zeilen (nicht importiert)")]
    [InlineData("en", "Import.ColProblem", "Problem")]
    [InlineData("de", "Import.ColProblem", "Problem")]
    [InlineData("en", "Import.ColRawLine", "Raw line")]
    [InlineData("de", "Import.ColRawLine", "Rohzeile")]
    [InlineData("en", "Import.ImportingBusyText", "Importing...")]
    [InlineData("de", "Import.ImportingBusyText", "Wird importiert…")]
    [InlineData("en", "Import.HistoryTitle", "Import history")]
    [InlineData("de", "Import.HistoryTitle", "Import-Verlauf")]
    [InlineData("en", "Import.ColWhen", "When")]
    [InlineData("de", "Import.ColWhen", "Wann")]
    [InlineData("en", "Import.ColFile", "File")]
    [InlineData("de", "Import.ColFile", "Datei")]
    [InlineData("en", "Import.ColFormat", "Format")]
    [InlineData("de", "Import.ColFormat", "Format")]
    [InlineData("en", "Import.ColImported", "Imported")]
    [InlineData("de", "Import.ColImported", "Importiert")]
    [InlineData("en", "Import.ColDuplicates", "Duplicates")]
    [InlineData("de", "Import.ColDuplicates", "Duplikate")]
    [InlineData("en", "Import.ColErrors", "Errors")]
    [InlineData("de", "Import.ColErrors", "Fehler")]
    [InlineData("en", "Import.ColImportedBy", "By")]
    [InlineData("de", "Import.ColImportedBy", "Benutzer")]
    [InlineData("en", "Import.ImportFailedTitle", "Import failed")]
    [InlineData("de", "Import.ImportFailedTitle", "Import fehlgeschlagen")]
    [InlineData("en", "Import.ImportCompleteTitle", "Import complete")]
    [InlineData("de", "Import.ImportCompleteTitle", "Import abgeschlossen")]
    public void GetString_ImportKey_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_ImportProviderCaption_FormatsTheProviderIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.ProviderCaption", "DKB"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.ProviderCaption", "DKB"];
        }

        // Assert
        Assert.StartsWith("Provider: DKB", en);
        Assert.StartsWith("Anbieter: DKB", de);
    }

    [Fact]
    public void GetString_ImportPreviewTitle_FormatsFileAndParserIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.PreviewTitle", "statement.csv", "DKB"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.PreviewTitle", "statement.csv", "DKB"];
        }

        // Assert
        Assert.Equal("Preview — statement.csv (DKB)", en);
        Assert.Equal("Vorschau — statement.csv (DKB)", de);
    }

    [Fact]
    public void GetString_ImportNewCount_FormatsZeroExactlyForE2E()
    {
        // Arrange / Act — the E2E suite asserts on the exact rendered text "0 new".
        string en;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.NewCount", 0];
        }

        // Assert
        Assert.Equal("0 new", en);
    }

    [Fact]
    public void GetString_ImportDuplicatesCount_FormatsBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.DuplicatesCount", 2];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.DuplicatesCount", 2];
        }

        // Assert
        Assert.Equal("2 duplicates", en);
        Assert.Equal("2 Duplikate", de);
    }

    [Fact]
    public void GetString_ImportProblemRowsCount_FormatsBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.ProblemRowsCount", 1];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.ProblemRowsCount", 1];
        }

        // Assert
        Assert.Equal("1 problem rows", en);
        Assert.Equal("1 fehlerhafte Zeilen", de);
    }

    [Fact]
    public void GetString_ImportButton_ContainsImportForE2EAndFormatsGermanNaturally()
    {
        // Arrange / Act — the E2E suite clicks a button matching "Import" (non-exact), so the
        // English rendering must keep containing that word.
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.ImportButton", 3];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.ImportButton", 3];
        }

        // Assert
        Assert.Contains("Import", en);
        Assert.Equal("Import 3 transactions", en);
        Assert.Equal("3 Transaktionen importieren", de);
    }

    [Fact]
    public void GetString_ImportCouldNotReadFile_FormatsTheExceptionMessageIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.CouldNotReadFile", "disk full"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.CouldNotReadFile", "disk full"];
        }

        // Assert
        Assert.Equal("Could not read the file: disk full", en);
        Assert.Equal("Datei konnte nicht gelesen werden: disk full", de);
    }

    [Fact]
    public void GetString_ImportCompleteMessage_FormatsTheCountsIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Import.ImportCompleteMessage", 5, 2];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Import.ImportCompleteMessage", 5, 2];
        }

        // Assert
        Assert.Equal("5 imported, 2 duplicates skipped.", en);
        Assert.Equal("5 importiert, 2 Duplikate übersprungen.", de);
    }

    [Theory]
    [InlineData("en", "Dashboard.Title", "Dashboard")]
    [InlineData("de", "Dashboard.Title", "Übersicht")]
    [InlineData("en", "Dashboard.Income", "Income")]
    [InlineData("de", "Dashboard.Income", "Einnahmen")]
    [InlineData("en", "Dashboard.Expenses", "Expenses")]
    [InlineData("de", "Dashboard.Expenses", "Ausgaben")]
    [InlineData("en", "Dashboard.Net", "Net")]
    [InlineData("de", "Dashboard.Net", "Saldo")]
    [InlineData("en", "Dashboard.SpendingByCategory", "Spending by category")]
    [InlineData("de", "Dashboard.SpendingByCategory", "Ausgaben nach Kategorie")]
    [InlineData("en", "Dashboard.NoExpensesThisMonth", "No expenses recorded for this month.")]
    [InlineData("de", "Dashboard.NoExpensesThisMonth", "Keine Ausgaben für diesen Monat erfasst.")]
    [InlineData("en", "Dashboard.ColCategory", "Category")]
    [InlineData("de", "Dashboard.ColCategory", "Kategorie")]
    [InlineData("en", "Dashboard.ReviewThemLink", "review them")]
    [InlineData("de", "Dashboard.ReviewThemLink", "jetzt prüfen")]
    [InlineData("en", "Dashboard.NetWorth", "Net worth")]
    [InlineData("de", "Dashboard.NetWorth", "Nettovermögen")]
    [InlineData("en", "Dashboard.SetOneLink", "set one")]
    [InlineData("de", "Dashboard.SetOneLink", "jetzt festlegen")]
    [InlineData("en", "Dashboard.ToIncludeThem", " to include them.")]
    [InlineData("de", "Dashboard.ToIncludeThem", " und sie werden mitgezählt.")]
    [InlineData("en", "Dashboard.NoBudgetsSet", "No budgets set —")]
    [InlineData("de", "Dashboard.NoBudgetsSet", "Keine Budgets festgelegt —")]
    [InlineData("en", "Dashboard.AddOneLink", "add one")]
    [InlineData("de", "Dashboard.AddOneLink", "eines hinzufügen")]
    [InlineData("en", "Dashboard.HoldingsLink", "Holdings")]
    [InlineData("de", "Dashboard.HoldingsLink", "Bestände")]
    [InlineData("en", "Common.NoPriceYet", "no price yet")]
    [InlineData("de", "Common.NoPriceYet", "noch kein Kurs")]
    [InlineData("en", "Dashboard.HoldingsPageLink", "holdings page")]
    [InlineData("de", "Dashboard.HoldingsPageLink", "Bestandsseite")]
    [InlineData("en", "Dashboard.UpcomingOverdue", "Upcoming & overdue")]
    [InlineData("de", "Dashboard.UpcomingOverdue", "Anstehend & überfällig")]
    [InlineData("en", "Dashboard.NothingUpcoming", "Nothing upcoming —")]
    [InlineData("de", "Dashboard.NothingUpcoming", "Nichts anstehend —")]
    [InlineData("en", "Dashboard.AddPlannedItemsLink", "add planned items")]
    [InlineData("de", "Dashboard.AddPlannedItemsLink", "geplante Posten hinzufügen")]
    public void GetString_DashboardKey_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_DashboardCountedNouns_KeepThePluralHedgeInBothCultures()
    {
        // Arrange / Act — .resx has no plural support, so counted nouns carry an explicit
        // "(s)" / "(en)" hedge rather than silently reading wrong at count == 1.
        string enAwaiting;
        string deAwaiting;
        string enUncategorized;
        string deUncategorized;
        using (CultureScope.UiOnly("en"))
        {
            enAwaiting = FinanceLocalizer.Create()["Dashboard.AwaitingConversion", 3];
            enUncategorized = FinanceLocalizer.Create()["Dashboard.UncategorizedCount", 7];
        }
        using (CultureScope.UiOnly("de"))
        {
            deAwaiting = FinanceLocalizer.Create()["Dashboard.AwaitingConversion", 3];
            deUncategorized = FinanceLocalizer.Create()["Dashboard.UncategorizedCount", 7];
        }

        // Assert
        Assert.Equal(
            "3 transaction(s) this month are awaiting EUR conversion and are not yet included in the totals.",
            enAwaiting
        );
        Assert.Equal(
            "3 Transaktion(en) in diesem Monat warten auf die EUR-Umrechnung und sind noch nicht in den Summen enthalten.",
            deAwaiting
        );
        Assert.Equal("7 transaction(s) are uncategorized —", enUncategorized);
        Assert.Equal("7 Transaktion(en) sind unkategorisiert —", deUncategorized);
    }

    [Fact]
    public void GetString_DashboardAccountsWithoutBalance_FormatsTheCountIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Dashboard.AccountsWithoutBalance", 2];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Dashboard.AccountsWithoutBalance", 2];
        }

        // Assert
        Assert.Equal("2 account(s) have no balance set —", en);
        Assert.Equal("Bei 2 Konto/Konten fehlt der Kontostand —", de);
    }

    [Fact]
    public void GetString_DashboardPricesFrom_FormatsTheDateIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Dashboard.PricesFrom", "20.08.2026"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Dashboard.PricesFrom", "20.08.2026"];
        }

        // Assert
        Assert.Equal("Prices from 20.08.2026 — refresh on the", en);
        Assert.Equal("Kurse vom 20.08.2026 — Aktualisierung auf der", de);
    }

    [Theory]
    [InlineData("en", "Common.Add", "Add")]
    [InlineData("de", "Common.Add", "Hinzufügen")]
    [InlineData("en", "Common.Edit", "Edit")]
    [InlineData("de", "Common.Edit", "Bearbeiten")]
    [InlineData("en", "Common.Apply", "Apply")]
    [InlineData("de", "Common.Apply", "Anwenden")]
    [InlineData("en", "Common.No", "No")]
    [InlineData("de", "Common.No", "Nein")]
    [InlineData("en", "Common.Owner", "Owner")]
    [InlineData("de", "Common.Owner", "Inhaber")]
    [InlineData("en", "Common.Search", "Search")]
    [InlineData("de", "Common.Search", "Suche")]
    [InlineData("en", "Common.From", "From")]
    [InlineData("de", "Common.From", "Von")]
    [InlineData("en", "Common.To", "To")]
    [InlineData("de", "Common.To", "Bis")]
    [InlineData("en", "Common.AllPlaceholder", "All")]
    [InlineData("de", "Common.AllPlaceholder", "Alle")]
    [InlineData("en", "Common.SelectCategoryPlaceholder", "Select category")]
    [InlineData("de", "Common.SelectCategoryPlaceholder", "Kategorie auswählen")]
    [InlineData("en", "Common.DeleteFailedTitle", "Delete failed")]
    [InlineData("de", "Common.DeleteFailedTitle", "Löschen fehlgeschlagen")]
    [InlineData("en", "Transactions.CreateRule", "Create rule")]
    [InlineData("de", "Transactions.CreateRule", "Regel erstellen")]
    [InlineData("en", "Transactions.SearchPlaceholder", "Description or counterparty")]
    [InlineData("de", "Transactions.SearchPlaceholder", "Beschreibung oder Zahlungspartner")]
    [InlineData("en", "Transactions.LinkAsTransfer", "Link as transfer")]
    [InlineData("de", "Transactions.LinkAsTransfer", "Als Umbuchung verknüpfen")]
    [InlineData("en", "Transactions.UncategorizedPlaceholder", "Uncategorized")]
    [InlineData("de", "Transactions.UncategorizedPlaceholder", "Unkategorisiert")]
    [InlineData("en", "Transactions.TransferBadge", "Transfer")]
    [InlineData("de", "Transactions.TransferBadge", "Umbuchung")]
    [InlineData("en", "Transactions.UnlinkTransfer", "Unlink transfer")]
    [InlineData("de", "Transactions.UnlinkTransfer", "Verknüpfung aufheben")]
    [InlineData("en", "Transactions.LinkedMessage", "Transactions linked as transfer.")]
    [InlineData("de", "Transactions.LinkedMessage", "Transaktionen als Umbuchung verknüpft.")]
    [InlineData("en", "Rules.PageTitle", "Categorization rules")]
    [InlineData("de", "Rules.PageTitle", "Kategorisierungsregeln")]
    [InlineData("en", "Rules.EmptyTitle", "No rules yet")]
    [InlineData("de", "Rules.EmptyTitle", "Noch keine Regeln")]
    [InlineData("en", "Rules.ColCounterpartyContains", "Counterparty contains")]
    [InlineData("de", "Rules.ColCounterpartyContains", "Zahlungspartner enthält")]
    [InlineData("en", "Rules.ColSource", "Source")]
    [InlineData("de", "Rules.ColSource", "Quelle")]
    [InlineData("en", "Rules.AddRuleTitle", "Add rule")]
    [InlineData("de", "Rules.AddRuleTitle", "Regel hinzufügen")]
    [InlineData("en", "Rules.MissingCategoryMessage", "Pick a category first.")]
    [InlineData("de", "Rules.MissingCategoryMessage", "Zuerst eine Kategorie auswählen.")]
    [InlineData("en", "Budgets.SetBudgetTitle", "Set a budget")]
    [InlineData("de", "Budgets.SetBudgetTitle", "Budget festlegen")]
    [InlineData("en", "Budgets.CurrentBudgetsHeading", "Current budgets")]
    [InlineData("de", "Budgets.CurrentBudgetsHeading", "Aktuelle Budgets")]
    [InlineData("en", "Budgets.EmptyTitle", "No budgets set yet")]
    [InlineData("de", "Budgets.EmptyTitle", "Noch keine Budgets festgelegt")]
    [InlineData("en", "Budgets.ColMonthlyLimit", "Monthly limit")]
    [InlineData("de", "Budgets.ColMonthlyLimit", "Monatliches Limit")]
    [InlineData("en", "Budgets.DeletedCategory", "(deleted category)")]
    [InlineData("de", "Budgets.DeletedCategory", "(gelöschte Kategorie)")]
    [InlineData("en", "Budgets.SavedTitle", "Budget saved")]
    [InlineData("de", "Budgets.SavedTitle", "Budget gespeichert")]
    [InlineData("en", "Budgets.RemovedTitle", "Budget removed")]
    [InlineData("de", "Budgets.RemovedTitle", "Budget entfernt")]
    public void GetString_B6Key_ReturnsTheCultureSpecificValue(
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

    /// <summary>
    /// Every enum member rendered as UI text needs its own key, since the fallback would be the
    /// bare C# identifier. A missing member surfaces only when that value happens to appear in a
    /// grid, so pin the whole set.
    /// </summary>
    [Theory]
    [InlineData("en", "Enum.CategorySource.Provider", "Provider")]
    [InlineData("de", "Enum.CategorySource.Provider", "Anbieter")]
    [InlineData("en", "Enum.CategorySource.Rule", "Rule")]
    [InlineData("de", "Enum.CategorySource.Rule", "Regel")]
    [InlineData("en", "Enum.CategorySource.Ai", "Ai")]
    [InlineData("de", "Enum.CategorySource.Ai", "KI")]
    [InlineData("en", "Enum.CategorySource.Manual", "Manual")]
    [InlineData("de", "Enum.CategorySource.Manual", "Manuell")]
    [InlineData("en", "Enum.CategoryRuleSource.Manual", "Manual")]
    [InlineData("de", "Enum.CategoryRuleSource.Manual", "Manuell")]
    [InlineData("en", "Enum.CategoryRuleSource.LearnedFromCorrection", "Learned from correction")]
    [InlineData("de", "Enum.CategoryRuleSource.LearnedFromCorrection", "Aus Korrektur gelernt")]
    public void GetString_EnumDisplayKey_ReturnsTheCultureSpecificValue(
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

    /// <summary>
    /// Guards the plan's "baked English article" trap: these subjects are substituted into the
    /// design system's shared "Delete {0}? …" / "{0} löschen? …" template, so the German value has
    /// to be a grammatical object on its own, not a translation of an English fragment.
    /// </summary>
    [Fact]
    public void GetString_DeleteConfirmSubjects_ComposeGrammaticallyInBothCultures()
    {
        // Arrange / Act
        string enBudget;
        string deBudget;
        string enRule;
        string deRule;
        using (CultureScope.UiOnly("en"))
        {
            enBudget = FinanceLocalizer.Create()[
                "Budgets.DeleteConfirmSubject",
                "Food › Groceries"
            ];
            enRule = FinanceLocalizer.Create()["Rules.DeleteConfirmSubject"];
        }
        using (CultureScope.UiOnly("de"))
        {
            deBudget = FinanceLocalizer.Create()[
                "Budgets.DeleteConfirmSubject",
                "Food › Groceries"
            ];
            deRule = FinanceLocalizer.Create()["Rules.DeleteConfirmSubject"];
        }

        // Assert — German uses its own quotation marks and article, not a literal translation.
        Assert.Equal("the budget for “Food › Groceries”", enBudget);
        Assert.Equal("das Budget für „Food › Groceries“", deBudget);
        Assert.Equal("this rule", enRule);
        Assert.Equal("diese Regel", deRule);
    }

    [Theory]
    [InlineData("en", "Common.Cancel", "Cancel")]
    [InlineData("de", "Common.Cancel", "Abbrechen")]
    [InlineData("en", "Common.Yes", "yes")]
    [InlineData("de", "Common.Yes", "ja")]
    [InlineData("en", "Planning.MatchNow", "Match now")]
    [InlineData("de", "Planning.MatchNow", "Jetzt zuordnen")]
    [InlineData("en", "Planning.ThisMonthsPlan", "This month's plan")]
    [InlineData("de", "Planning.ThisMonthsPlan", "Plan für diesen Monat")]
    [InlineData("en", "Planning.ColDue", "Due")]
    [InlineData("de", "Planning.ColDue", "Fällig")]
    [InlineData("en", "Planning.ColSchedule", "Schedule")]
    [InlineData("de", "Planning.ColSchedule", "Zeitplan")]
    [InlineData("en", "Planning.Match", "Match")]
    [InlineData("de", "Planning.Match", "Zuordnen")]
    [InlineData("en", "Planning.Unmatch", "Unmatch")]
    [InlineData("de", "Planning.Unmatch", "Zuordnung lösen")]
    [InlineData("en", "Planning.AddItemTitle", "Add planned item")]
    [InlineData("de", "Planning.AddItemTitle", "Geplanten Posten hinzufügen")]
    [InlineData("en", "Planning.EditItemTitle", "Edit planned item")]
    [InlineData("de", "Planning.EditItemTitle", "Geplanten Posten bearbeiten")]
    [InlineData("en", "Planning.TypeExpense", "Expense")]
    [InlineData("de", "Planning.TypeExpense", "Ausgabe")]
    [InlineData("en", "Planning.TypeIncome", "Income")]
    [InlineData("de", "Planning.TypeIncome", "Einnahme")]
    [InlineData("en", "Planning.ActiveLabel", "Active")]
    [InlineData("de", "Planning.ActiveLabel", "Aktiv")]
    [InlineData("en", "Planning.MatchedTitle", "Matched")]
    [InlineData("de", "Planning.MatchedTitle", "Zugeordnet")]
    [InlineData("en", "Planning.ItemSavedTitle", "Planned item saved")]
    [InlineData("de", "Planning.ItemSavedTitle", "Geplanter Posten gespeichert")]
    [InlineData("en", "Enum.PlannedFrequency.OneTime", "One-time")]
    [InlineData("de", "Enum.PlannedFrequency.OneTime", "Einmalig")]
    [InlineData("en", "Enum.PlannedFrequency.Monthly", "Monthly")]
    [InlineData("de", "Enum.PlannedFrequency.Monthly", "Monatlich")]
    [InlineData("en", "Enum.PlannedFrequency.Quarterly", "Quarterly")]
    [InlineData("de", "Enum.PlannedFrequency.Quarterly", "Vierteljährlich")]
    [InlineData("en", "Enum.PlannedFrequency.Yearly", "Yearly")]
    [InlineData("de", "Enum.PlannedFrequency.Yearly", "Jährlich")]
    [InlineData("en", "Enum.PlannedOccurrenceStatus.Pending", "Pending")]
    [InlineData("de", "Enum.PlannedOccurrenceStatus.Pending", "Ausstehend")]
    [InlineData("en", "Enum.PlannedOccurrenceStatus.Matched", "Matched")]
    [InlineData("de", "Enum.PlannedOccurrenceStatus.Matched", "Zugeordnet")]
    [InlineData("en", "Enum.PlannedOccurrenceStatus.Overdue", "Overdue")]
    [InlineData("de", "Enum.PlannedOccurrenceStatus.Overdue", "Überfällig")]
    [InlineData("en", "Enum.PlannedOccurrenceStatus.Skipped", "Skipped")]
    [InlineData("de", "Enum.PlannedOccurrenceStatus.Skipped", "Übersprungen")]
    [InlineData("en", "Crypto.PageTitle", "Crypto holdings")]
    [InlineData("de", "Crypto.PageTitle", "Krypto-Bestände")]
    [InlineData("en", "Crypto.RefreshValues", "Refresh values")]
    [InlineData("de", "Crypto.RefreshValues", "Werte aktualisieren")]
    [InlineData("en", "Crypto.SetHoldingTitle", "Set a holding")]
    [InlineData("de", "Crypto.SetHoldingTitle", "Bestand festlegen")]
    [InlineData("en", "Crypto.CoinGeckoIdLabel", "CoinGecko id")]
    [InlineData("de", "Crypto.CoinGeckoIdLabel", "CoinGecko-ID")]
    [InlineData("en", "Crypto.QuantityLabel", "Quantity")]
    [InlineData("de", "Crypto.QuantityLabel", "Menge")]
    [InlineData("en", "Crypto.CurrentHoldingsHeading", "Current holdings")]
    [InlineData("de", "Crypto.CurrentHoldingsHeading", "Aktuelle Bestände")]
    [InlineData("en", "Crypto.EmptyTitle", "No holdings yet")]
    [InlineData("de", "Crypto.EmptyTitle", "Noch keine Bestände")]
    [InlineData("en", "Crypto.ColPrice", "Price")]
    [InlineData("de", "Crypto.ColPrice", "Kurs")]
    [InlineData("en", "Crypto.ColValue", "Value")]
    [InlineData("de", "Crypto.ColValue", "Wert")]
    [InlineData("en", "Crypto.HoldingSavedTitle", "Holding saved")]
    [InlineData("de", "Crypto.HoldingSavedTitle", "Bestand gespeichert")]
    [InlineData("en", "Crypto.PricesUnavailableTitle", "Prices unavailable")]
    [InlineData("de", "Crypto.PricesUnavailableTitle", "Kurse nicht verfügbar")]
    [InlineData(
        "en",
        "Crypto.PricesUnavailableMessage",
        "CoinGecko could not be reached — cached prices were used."
    )]
    [InlineData(
        "de",
        "Crypto.PricesUnavailableMessage",
        "CoinGecko war nicht erreichbar — zwischengespeicherte Kurse wurden verwendet."
    )]
    public void GetString_B7Key_ReturnsTheCultureSpecificValue(
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

    /// <summary>
    /// <c>ScheduleLabel</c> composes a localized frequency into a localized sentence frame around a
    /// date. Both the frame and the nested frequency have to swap culture together, so pin the
    /// assembled result rather than the two fragments separately.
    /// </summary>
    [Fact]
    public void GetString_PlanningScheduleLabels_ComposeInBothCultures()
    {
        // Arrange / Act
        string enOnce;
        string deOnce;
        string enRecurring;
        string deRecurring;
        using (CultureScope.UiOnly("en"))
        {
            var l = FinanceLocalizer.Create();
            enOnce = l["Planning.ScheduleOnce", "24.08.2026"];
            enRecurring = l[
                "Planning.ScheduleRecurring",
                l["Enum.PlannedFrequency.Monthly"],
                "24.08.2026"
            ];
        }
        using (CultureScope.UiOnly("de"))
        {
            var l = FinanceLocalizer.Create();
            deOnce = l["Planning.ScheduleOnce", "24.08.2026"];
            deRecurring = l[
                "Planning.ScheduleRecurring",
                l["Enum.PlannedFrequency.Monthly"],
                "24.08.2026"
            ];
        }

        // Assert
        Assert.Equal("Once, 24.08.2026", enOnce);
        Assert.Equal("Einmalig, 24.08.2026", deOnce);
        Assert.Equal("Monthly from 24.08.2026", enRecurring);
        Assert.Equal("Monatlich ab 24.08.2026", deRecurring);
    }

    /// <summary>
    /// Both of this batch's delete-confirm subjects feed the shared "Delete {0}? …" /
    /// "{0} löschen? …" template. The crypto one is the harder case: English puts the noun after
    /// the symbol ("the BTC holding"), so a literal translation would strand it — German has to
    /// reorder.
    /// </summary>
    [Fact]
    public void GetString_B7DeleteConfirmSubjects_ComposeGrammaticallyInBothCultures()
    {
        // Arrange / Act
        string enPlanning;
        string dePlanning;
        string enCrypto;
        string deCrypto;
        using (CultureScope.UiOnly("en"))
        {
            var l = FinanceLocalizer.Create();
            enPlanning = l["Planning.DeleteConfirmSubject", "Rent"];
            enCrypto = l["Crypto.DeleteConfirmSubject", "BTC"];
        }
        using (CultureScope.UiOnly("de"))
        {
            var l = FinanceLocalizer.Create();
            dePlanning = l["Planning.DeleteConfirmSubject", "Miete"];
            deCrypto = l["Crypto.DeleteConfirmSubject", "BTC"];
        }

        // Assert
        Assert.Equal("the planned item “Rent”", enPlanning);
        Assert.Equal("den geplanten Posten „Miete“", dePlanning);
        Assert.Equal("the BTC holding", enCrypto);
        Assert.Equal("den BTC-Bestand", deCrypto);
    }

    #region Assembled multi-fragment sentences

    // Several UI strings are split across keys so a link or a bold value can sit mid-sentence.
    // Pinning the fragments individually is not enough: each fragment can be a correct translation
    // while the assembled sentence is ungrammatical, which is exactly what happened to the
    // rule-offer prompt (German strands the verb if the "als" complement follows it) and to the
    // net-worth hint (a stray space before the German comma). These tests assemble the fragments
    // the same way the Razor does and assert the whole sentence.

    /// <summary>
    /// Mirrors <c>Transactions.razor</c>: prefix, bold counterparty, infix, bold category, suffix —
    /// with no whitespace between the bold category and the suffix.
    /// </summary>
    private static string AssembleRuleOffer(string counterparty, string category)
    {
        var l = FinanceLocalizer.Create();
        return $"{l["Transactions.RuleOfferPrefix"]} {counterparty} "
            + $"{l["Transactions.RuleOfferInfix"]} {category}{l["Transactions.RuleOfferSuffix"]}";
    }

    [Fact]
    public void RuleOfferPrompt_AssemblesGrammaticallyInBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = AssembleRuleOffer("REWE", "Groceries");
        }
        using (CultureScope.UiOnly("de"))
        {
            de = AssembleRuleOffer("REWE", "Lebensmittel");
        }

        // Assert — German puts the "als …" complement before the infinitive, so the verb has to
        // land after the category, not between the counterparty and it.
        Assert.Equal("Always categorize REWE as Groceries?", en);
        Assert.Equal("Immer REWE als Lebensmittel kategorisieren?", de);
    }

    /// <summary>
    /// Mirrors <c>Dashboard.razor</c>: the count fragment, a space, the link text, then the
    /// trailing fragment with <em>no</em> separating space — each culture supplies its own.
    /// </summary>
    private static string AssembleNetWorthHint(int accounts)
    {
        var l = FinanceLocalizer.Create();
        return $"{l["Dashboard.AccountsWithoutBalance", accounts]} "
            + $"{l["Dashboard.SetOneLink"]}{l["Dashboard.ToIncludeThem"]}";
    }

    [Fact]
    public void NetWorthHint_AssemblesWithoutStraySpacesInBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = AssembleNetWorthHint(2);
        }
        using (CultureScope.UiOnly("de"))
        {
            de = AssembleNetWorthHint(2);
        }

        // Assert
        Assert.Equal("2 account(s) have no balance set — set one to include them.", en);
        Assert.Equal(
            "Bei 2 Konto/Konten fehlt der Kontostand — jetzt festlegen und sie werden mitgezählt.",
            de
        );
        Assert.DoesNotContain(" ,", de);
        Assert.DoesNotContain("  ", en);
    }

    #endregion

    [Theory]
    [InlineData("en", "Accounts.ShowDeactivated", "Show deactivated")]
    [InlineData("de", "Accounts.ShowDeactivated", "Deaktivierte anzeigen")]
    [InlineData("en", "Accounts.StatusActive", "Active")]
    [InlineData("de", "Accounts.StatusActive", "Aktiv")]
    [InlineData("en", "Accounts.StatusDeactivated", "Deactivated")]
    [InlineData("de", "Accounts.StatusDeactivated", "Deaktiviert")]
    [InlineData("en", "Accounts.AddTitle", "Add account")]
    [InlineData("de", "Accounts.AddTitle", "Konto hinzufügen")]
    [InlineData("en", "Accounts.EditTitle", "Edit account")]
    [InlineData("de", "Accounts.EditTitle", "Konto bearbeiten")]
    [InlineData("en", "Accounts.SharedLabel", "Shared")]
    [InlineData("de", "Accounts.SharedLabel", "Gemeinsam")]
    [InlineData("en", "Accounts.ConnectionLabel", "Connection")]
    [InlineData("de", "Accounts.ConnectionLabel", "Verbindung")]
    [InlineData("en", "Accounts.BankAccountLabel", "Bank account")]
    [InlineData("de", "Accounts.BankAccountLabel", "Bankkonto")]
    [InlineData("en", "Accounts.ActionFailedTitle", "Action failed")]
    [InlineData("de", "Accounts.ActionFailedTitle", "Aktion fehlgeschlagen")]
    [InlineData("en", "Accounts.DeactivatedMessage", "Account deactivated")]
    [InlineData("de", "Accounts.DeactivatedMessage", "Konto deaktiviert")]
    [InlineData("en", "Accounts.PermanentlyDeletedMessage", "Account permanently deleted")]
    [InlineData("de", "Accounts.PermanentlyDeletedMessage", "Konto endgültig gelöscht")]
    [InlineData("en", "Accounts.HardDeleteTitle", "Delete account permanently")]
    [InlineData("de", "Accounts.HardDeleteTitle", "Konto endgültig löschen")]
    [InlineData("en", "Enum.AccountType.Checking", "Checking")]
    [InlineData("de", "Enum.AccountType.Checking", "Girokonto")]
    [InlineData("en", "Enum.AccountType.CreditCard", "Credit card")]
    [InlineData("de", "Enum.AccountType.CreditCard", "Kreditkarte")]
    [InlineData("en", "Enum.AccountType.Crypto", "Crypto")]
    [InlineData("de", "Enum.AccountType.Crypto", "Krypto")]
    [InlineData("en", "Enum.AccountType.MultiCurrency", "Multi-currency")]
    [InlineData("de", "Enum.AccountType.MultiCurrency", "Multiwährung")]
    [InlineData("en", "Enum.SyncMethod.CsvUpload", "CSV upload")]
    [InlineData("de", "Enum.SyncMethod.CsvUpload", "CSV-Upload")]
    [InlineData("en", "Enum.SyncMethod.Api", "API")]
    [InlineData("de", "Enum.SyncMethod.Api", "API")]
    public void GetString_B8AccountsKey_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_AccountsHardDeleteConfirm_ComposesTheImpactSentenceInBothCultures()
    {
        // Arrange / Act — {1} is the already-assembled impact sentence, so the frame must not
        // introduce punctuation that collides with the sentence's own full stop.
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()[
                "Accounts.HardDeleteConfirm",
                "Joint account",
                "Deletes the account."
            ];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()[
                "Accounts.HardDeleteConfirm",
                "Gemeinschaftskonto",
                "Löscht das Konto."
            ];
        }

        // Assert
        Assert.Equal(
            "Permanently delete 'Joint account'? Deletes the account. This cannot be undone.",
            en
        );
        Assert.Equal(
            "„Gemeinschaftskonto“ endgültig löschen? Löscht das Konto. Das kann nicht rückgängig gemacht werden.",
            de
        );
    }

    [Theory]
    [InlineData("en", "Connections.EbAppTitle", "Enable Banking application")]
    [InlineData("de", "Connections.EbAppTitle", "Enable-Banking-Anwendung")]
    [InlineData("en", "Connections.ApplicationIdLabel", "Application id")]
    [InlineData("de", "Connections.ApplicationIdLabel", "Anwendungs-ID")]
    [InlineData("en", "Connections.PrivateKeyLabel", "Private key (PEM)")]
    [InlineData("de", "Connections.PrivateKeyLabel", "Privater Schlüssel (PEM)")]
    [InlineData("en", "Connections.AddTitle", "Add connection")]
    [InlineData("de", "Connections.AddTitle", "Verbindung hinzufügen")]
    [InlineData("en", "Connections.LabelLabel", "Label")]
    [InlineData("de", "Connections.LabelLabel", "Bezeichnung")]
    [InlineData("en", "Connections.EnvironmentLabel", "Environment")]
    [InlineData("de", "Connections.EnvironmentLabel", "Umgebung")]
    [InlineData("en", "Connections.EmptyTitle", "No connections yet")]
    [InlineData("de", "Connections.EmptyTitle", "Noch keine Verbindungen")]
    [InlineData("en", "Connections.UnknownUser", "unknown user")]
    [InlineData("de", "Connections.UnknownUser", "unbekannter Benutzer")]
    [InlineData("en", "Connections.NoOwner", "no owner")]
    [InlineData("de", "Connections.NoOwner", "kein Inhaber")]
    [InlineData("en", "Connections.SyncBalances", "Sync balances")]
    [InlineData("de", "Connections.SyncBalances", "Guthaben abgleichen")]
    [InlineData("en", "Connections.Connect", "Connect")]
    [InlineData("de", "Connections.Connect", "Verbinden")]
    [InlineData("en", "Connections.Reconnect", "Reconnect")]
    [InlineData("de", "Connections.Reconnect", "Neu verbinden")]
    [InlineData("en", "Connections.ConsentPending", "pending")]
    [InlineData("de", "Connections.ConsentPending", "ausstehend")]
    [InlineData("en", "Connections.ConsentNotConnected", "not connected")]
    [InlineData("de", "Connections.ConsentNotConnected", "nicht verbunden")]
    [InlineData("en", "Connections.CannotConnectTitle", "Cannot connect")]
    [InlineData("de", "Connections.CannotConnectTitle", "Verbinden nicht möglich")]
    [InlineData("en", "Connections.ConsentInvalidMessage", "Missing code or state.")]
    [InlineData("de", "Connections.ConsentInvalidMessage", "Code oder State fehlt.")]
    [InlineData("en", "Enum.ProviderEnvironment.Production", "Production")]
    [InlineData("de", "Enum.ProviderEnvironment.Production", "Produktiv")]
    [InlineData("en", "Enum.ProviderEnvironment.Sandbox", "Sandbox")]
    [InlineData("de", "Enum.ProviderEnvironment.Sandbox", "Sandbox")]
    public void GetString_B8ConnectionsKey_ReturnsTheCultureSpecificValue(
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

    [Fact]
    public void GetString_ConnectionsDeleteConfirmSubject_ComposesGrammaticallyInBothCultures()
    {
        // Arrange / Act — feeds the shared "Delete {0}? …" / "{0} löschen? …" template, so the
        // German value carries its own article and quotation marks.
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Connections.DeleteConfirmSubject", "André – Wise"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Connections.DeleteConfirmSubject", "André – Wise"];
        }

        // Assert
        Assert.Equal("the connection “André – Wise”", en);
        Assert.Equal("die Verbindung „André – Wise“", de);
    }

    [Fact]
    public void GetString_ConnectionsConsentActiveUntil_FormatsTheDateIntoBothCultures()
    {
        // Arrange / Act
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            en = FinanceLocalizer.Create()["Connections.ConsentActiveUntil", "22.11.2026"];
        }
        using (CultureScope.UiOnly("de"))
        {
            de = FinanceLocalizer.Create()["Connections.ConsentActiveUntil", "22.11.2026"];
        }

        // Assert
        Assert.Equal("active until 22.11.2026", en);
        Assert.Equal("aktiv bis 22.11.2026", de);
    }

    [Theory]
    [InlineData("en", "Connections.LabelRequired", "A connection label is required.")]
    [InlineData(
        "de",
        "Connections.LabelRequired",
        "Eine Bezeichnung für die Verbindung ist erforderlich."
    )]
    [InlineData("en", "Connections.NotFound", "Connection not found.")]
    [InlineData("de", "Connections.NotFound", "Verbindung nicht gefunden.")]
    [InlineData("en", "Connections.WiseTokenOnly", "Only Wise connections use an API token.")]
    [InlineData(
        "de",
        "Connections.WiseTokenOnly",
        "Nur Wise-Verbindungen verwenden ein API-Token."
    )]
    [InlineData("en", "Connections.WiseTokenRequired", "The Wise API token must not be empty.")]
    [InlineData("de", "Connections.WiseTokenRequired", "Das Wise-API-Token darf nicht leer sein.")]
    [InlineData("en", "Connections.BalanceSyncWiseOnly", "Balance sync is Wise-only.")]
    [InlineData(
        "de",
        "Connections.BalanceSyncWiseOnly",
        "Der Guthabenabgleich ist nur für Wise verfügbar."
    )]
    [InlineData("en", "Sync.AccountNotFound", "Account not found.")]
    [InlineData("de", "Sync.AccountNotFound", "Konto nicht gefunden.")]
    [InlineData("en", "Sync.ImportOnlyAccount", "Account is import-only (no API sync).")]
    [InlineData(
        "de",
        "Sync.ImportOnlyAccount",
        "Konto ist nur für den Import vorgesehen (keine API-Synchronisierung)."
    )]
    public void GetString_BackendServiceKey_ReturnsTheCultureSpecificValue(
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

    [Theory]
    [InlineData("Sync.AccountNotLinked")]
    [InlineData("Sync.ConnectionGone")]
    public void SyncNavPointers_NameTheAccountsMenuEntryInTheMatchingLanguage(string key)
    {
        // Arrange / Act — AccountSyncService fills {0} with Nav.Accounts so the pointer always
        // names the menu entry the user actually sees. A German sentence ending in "(Einstellungen
        // → Accounts)" would be the failure mode, and only composing both halves catches it.
        string en;
        string de;
        using (CultureScope.UiOnly("en"))
        {
            var l = FinanceLocalizer.Create();
            en = l[key, l["Nav.Accounts"]];
        }
        using (CultureScope.UiOnly("de"))
        {
            var l = FinanceLocalizer.Create();
            de = l[key, l["Nav.Accounts"]];
        }

        // Assert
        Assert.EndsWith("(Settings → Accounts).", en, StringComparison.Ordinal);
        Assert.EndsWith("(Einstellungen → Konten).", de, StringComparison.Ordinal);
    }
}
