using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndreGoepel.FinanceApp.E2ETests.Infrastructure;

/// <summary>
/// Thin client over the MailHog HTTP API so tests can assert on emails the app actually delivered
/// (confirmation links, password-reset links, etc.).
/// </summary>
public sealed partial class MailHogClient(string baseUrl)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl) };

    /// <summary>Deletes every message so a test starts from a known-empty inbox.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync("api/v1/messages", ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Polls until an email addressed to <paramref name="toEmail"/> arrives, returning its decoded body.
    /// </summary>
    public async Task<string> WaitForMessageBodyAsync(
        string toEmail,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTime.UtcNow < deadline)
        {
            var body = await TryGetLatestBodyAsync(toEmail, ct);
            if (body is not null)
            {
                return body;
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException($"No email delivered to '{toEmail}' within the timeout.");
    }

    /// <summary>Waits for the newest email to <paramref name="toEmail"/> and extracts the first link matching a filter.</summary>
    public async Task<string> WaitForLinkAsync(
        string toEmail,
        string mustContain,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var body = await WaitForMessageBodyAsync(toEmail, timeout, ct);
        var links = LinkRegex()
            .Matches(body)
            .Select(m => WebUtility.HtmlDecode(m.Value))
            .Where(url => url.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (links.Count == 0)
        {
            throw new InvalidOperationException(
                $"No link containing '{mustContain}' found in email to '{toEmail}'. Body was:\n{body}"
            );
        }

        return links[0];
    }

    private async Task<string?> TryGetLatestBodyAsync(string toEmail, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(
            "api/v2/search?kind=to&query=" + Uri.EscapeDataString(toEmail),
            ct
        );

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            return null;
        }

        // MailHog returns newest first.
        var message = items[0];
        return DecodeBody(message);
    }

    private static string DecodeBody(JsonElement message)
    {
        var builder = new StringBuilder();

        if (
            message.TryGetProperty("Content", out var content)
            && content.ValueKind == JsonValueKind.Object
        )
        {
            AppendPart(builder, content);
        }

        // MailHog serializes "MIME": null for non-multipart messages.
        if (
            message.TryGetProperty("MIME", out var mime)
            && mime.ValueKind == JsonValueKind.Object
            && mime.TryGetProperty("Parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var part in parts.EnumerateArray())
            {
                AppendPart(builder, part);
            }
        }

        return builder.ToString();
    }

    // Appends one MIME part's decoded body; quoted-printable decoding only applies when the part declares that transfer encoding, since decoding unconditionally corrupts URLs like "userId=a1…".
    private static void AppendPart(StringBuilder builder, JsonElement part)
    {
        if (!part.TryGetProperty("Body", out var body))
        {
            return;
        }

        var text = body.GetString() ?? string.Empty;
        builder.AppendLine(IsQuotedPrintable(part) ? DecodeQuotedPrintable(text) : text);
    }

    // True when the part's Content-Transfer-Encoding header is quoted-printable.
    private static bool IsQuotedPrintable(JsonElement part)
    {
        if (
            !part.TryGetProperty("Headers", out var headers)
            || headers.ValueKind != JsonValueKind.Object
            || !headers.TryGetProperty("Content-Transfer-Encoding", out var cte)
            || cte.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        return cte.EnumerateArray()
            .Any(v =>
                string.Equals(v.GetString(), "quoted-printable", StringComparison.OrdinalIgnoreCase)
            );
    }

    // Undoes the quoted-printable encoding MailHog stores so URLs are reassembled.
    private static string DecodeQuotedPrintable(string input)
    {
        var unfolded = input.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty);
        return QuotedPrintableRegex()
            .Replace(unfolded, match => ((char)Convert.ToInt32(match.Value[1..], 16)).ToString());
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex LinkRegex();

    [GeneratedRegex("=[0-9A-Fa-f]{2}")]
    private static partial Regex QuotedPrintableRegex();
}
