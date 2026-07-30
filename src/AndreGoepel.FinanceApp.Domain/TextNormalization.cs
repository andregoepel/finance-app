using System.Text;

namespace AndreGoepel.FinanceApp.Domain;

/// <summary>
/// Text-normalization helpers shared across the domain wherever cosmetic
/// casing/whitespace differences between provider exports must not affect
/// matching — import dedup hashing and recurring-transaction grouping both
/// need "same text, different formatting" to collapse to one key.
/// </summary>
internal static class TextNormalization
{
    /// <summary>
    /// Lower-cases and collapses whitespace: leading/trailing whitespace is
    /// removed and any run of one or more whitespace characters (spaces,
    /// tabs, newlines, ...) between words becomes a single space.
    /// </summary>
    public static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
