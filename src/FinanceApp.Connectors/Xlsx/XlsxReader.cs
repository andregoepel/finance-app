using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FinanceApp.Connectors.Xlsx;

/// <summary>One worksheet row: 1-based row number and cell values keyed by column letter.</summary>
internal sealed record XlsxRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
{
    public string Cell(string column) => Cells.GetValueOrDefault(column, "");
}

/// <summary>
/// Minimal SpreadsheetML reader for provider exports (Revolut, Easy Bank ship
/// XLSX only): first worksheet, shared strings, inline strings, and numeric
/// values (dates arrive as OADate serials — see <see cref="TryParseOADate"/>).
/// No external dependency; the exports are small, flat tables.
/// </summary>
internal static class XlsxReader
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool LooksLikeWorkbook(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            return archive.GetEntry("xl/workbook.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public static List<XlsxRow> ReadFirstSheet(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(archive);

        var sheetEntry =
            archive
                .Entries.Where(entry =>
                    entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && !entry.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            ?? throw new InvalidDataException("The workbook contains no worksheet.");

        var sheet = LoadXml(sheetEntry);
        var rows = new List<XlsxRow>();
        foreach (var rowElement in sheet.Descendants(Main + "row"))
        {
            var rowNumber = (int?)rowElement.Attribute("r") ?? rows.Count + 1;
            var cells = new Dictionary<string, string>();
            foreach (var cellElement in rowElement.Elements(Main + "c"))
            {
                var reference = (string?)cellElement.Attribute("r") ?? "";
                var column = new string(reference.TakeWhile(char.IsLetter).ToArray());
                if (column.Length == 0)
                {
                    continue;
                }
                cells[column] = CellValue(cellElement, sharedStrings);
            }
            rows.Add(new XlsxRow(rowNumber, cells));
        }
        return rows;
    }

    /// <summary>Excel serial date (days since 1899-12-30) → date part.</summary>
    public static bool TryParseOADate(string value, out DateOnly date)
    {
        if (
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is > 20000 and < 80000
        )
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        date = default;
        return false;
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return cell.Element(Main + "is")?.Value ?? "";
        }

        var value = cell.Element(Main + "v")?.Value ?? "";
        if (type == "s" && int.TryParse(value, out var index) && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }
        return value;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        // Concatenate all text runs per item so formatted strings stay whole.
        return LoadXml(entry)
            .Root!.Elements()
            .Select(item =>
                string.Concat(
                    item.Descendants().Where(d => d.Name.LocalName == "t").Select(d => d.Value)
                )
            )
            .ToList();
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
