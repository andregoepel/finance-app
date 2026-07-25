using System.IO.Compression;
using AndreGoepel.FinanceApp.Connectors.Xlsx;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Xlsx;

public sealed class XlsxReaderTests
{
    [Fact]
    public void ReadFirstSheet_WorkbookWithNoWorksheet_ReturnsFailureResult()
    {
        // Arrange
        var content = WorkbookWithoutWorksheet();

        // Act
        var result = XlsxReader.ReadFirstSheet(content);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("The workbook contains no worksheet.", result.Error);
    }

    private static byte[] WorkbookWithoutWorksheet()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/workbook.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<workbook/>");
        }
        return stream.ToArray();
    }
}
