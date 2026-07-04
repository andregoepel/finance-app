using System.Text;

namespace AndreGoepel.FinanceApp.Connectors.Parsing;

/// <summary>
/// An uploaded statement export. Providers deliver either text (CSV) or binary
/// (XLSX) files, so parsers receive the raw bytes plus helpers for both shapes.
/// </summary>
public sealed record StatementFile(string FileName, byte[] Content)
{
    /// <summary>XLSX files are ZIP archives — detected via the PK signature.</summary>
    public bool IsZipArchive => Content.Length >= 4 && Content[0] == 0x50 && Content[1] == 0x4B;

    private string? decodedText;

    /// <summary>
    /// Text content for CSV parsers. Provider exports are UTF-8 except some
    /// German bank exports (Latin-1): strict UTF-8 first, Latin-1 fallback
    /// instead of importing replacement characters.
    /// </summary>
    public string DecodeText()
    {
        if (decodedText is not null)
        {
            return decodedText;
        }

        try
        {
            decodedText = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Content);
        }
        catch (DecoderFallbackException)
        {
            decodedText = Encoding.Latin1.GetString(Content);
        }
        return decodedText;
    }
}
