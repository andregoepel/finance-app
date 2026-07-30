using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// Idempotency key for imports: every import (CSV or API) must be re-runnable
/// without creating duplicates. The hash covers
/// <c>(accountId, bookingDate, amount, normalized description)</c>.
/// </summary>
public static class DedupHash
{
    public static string Compute(
        Guid accountId,
        DateOnly bookingDate,
        decimal amount,
        string description
    )
    {
        var payload = string.Join(
            '|',
            accountId.ToString("D"),
            bookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount.ToString(CultureInfo.InvariantCulture),
            NormalizeDescription(description)
        );
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// Lower-cases and collapses whitespace so cosmetic differences between
    /// exports of the same booking do not defeat deduplication.
    /// </summary>
    internal static string NormalizeDescription(string description) =>
        TextNormalization.NormalizeWhitespace(description);
}
