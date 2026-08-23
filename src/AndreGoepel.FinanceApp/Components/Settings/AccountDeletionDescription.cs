using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Resources;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Components.Settings;

/// <summary>
/// Turns an <see cref="AccountDeletionImpact"/> into the one sentence the hard-delete confirmation
/// dialog shows. This lives in the UI rather than on the domain record because everything it does
/// — picking singular or plural, joining a list with "and" — is a language rule: German needs
/// different plural forms and its own list conjunction, and the counted nouns have to agree with
/// the case the surrounding sentence frame imposes.
/// </summary>
internal static class AccountDeletionDescription
{
    public static string Build(AccountDeletionImpact impact, IStringLocalizer<Strings> l)
    {
        if (impact.IsAccountOnly)
        {
            return l["AccountDelete.AccountOnly"];
        }

        List<string> deleted = [];
        AddPhrase(deleted, impact.Transactions, "AccountDelete.Transactions", l);
        AddPhrase(deleted, impact.ImportBatches, "AccountDelete.ImportBatches", l);
        AddPhrase(deleted, impact.CryptoHoldings, "AccountDelete.CryptoHoldings", l);
        AddPhrase(deleted, impact.ReviewQueueEntries, "AccountDelete.ReviewEntries", l);

        List<string> kept = [];
        AddPhrase(kept, impact.TransfersUnlinked, "AccountDelete.Transfers", l);
        AddPhrase(kept, impact.PlannedMatchesCleared, "AccountDelete.PlannedMatches", l);
        AddPhrase(kept, impact.PlannedItemsDetached, "AccountDelete.PlannedItems", l);

        List<string> sentences =
        [
            deleted.Count > 0
                ? l["AccountDelete.DeletesAccountPlus", Join(deleted, l)]
                : l["AccountDelete.DeletesAccount"],
        ];
        if (kept.Count > 0)
        {
            sentences.Add(l["AccountDelete.Unlinks", Join(kept, l)]);
        }

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// Appends "{count} {noun}" for a non-zero count, choosing the singular or plural resource.
    /// Real plural selection rather than the "(s)" hedge used elsewhere: the count is known here,
    /// and the English wording already distinguished the two.
    /// </summary>
    private static void AddPhrase(
        List<string> phrases,
        int count,
        string keyPrefix,
        IStringLocalizer<Strings> l
    )
    {
        if (count > 0)
        {
            phrases.Add(l[count == 1 ? $"{keyPrefix}One" : $"{keyPrefix}Many", count]);
        }
    }

    /// <summary>Joins phrases as "a, b and c" — the conjunction itself is localized.</summary>
    private static string Join(IReadOnlyList<string> phrases, IStringLocalizer<Strings> l) =>
        phrases.Count == 1
            ? phrases[0]
            : l["Common.ListAnd", string.Join(", ", phrases.Take(phrases.Count - 1)), phrases[^1]];
}
