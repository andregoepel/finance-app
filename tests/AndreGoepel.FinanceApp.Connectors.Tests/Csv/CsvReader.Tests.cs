using AndreGoepel.FinanceApp.Connectors.Csv;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Csv;

public sealed class CsvReaderTests
{
    [Fact]
    public void Read_PlainFields_SplitsOnDelimiter()
    {
        // Act
        var records = CsvReader.Read("a,b,c\nd,e,f", ',');

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b", "c"], records[0].Fields);
        Assert.Equal(["d", "e", "f"], records[1].Fields);
    }

    [Fact]
    public void Read_QuotedFieldWithDelimiterAndEscapedQuote_ParsesAsOneField()
    {
        // Act
        var records = CsvReader.Read("\"a,b\",\"he said \"\"hi\"\"\"", ',');

        // Assert
        var record = Assert.Single(records);
        Assert.Equal(["a,b", "he said \"hi\""], record.Fields);
    }

    [Fact]
    public void Read_QuotedFieldWithLineBreak_KeepsRecordTogether()
    {
        // Act
        var records = CsvReader.Read("\"line1\nline2\",x\nnext,y", ',');

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("line1\nline2", records[0].Fields[0]);
        Assert.Equal(3, records[1].LineNumber);
    }

    [Fact]
    public void Read_BlankLines_AreSkipped()
    {
        // Act
        var records = CsvReader.Read("a;b\r\n\r\nc;d\r\n", ';');

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Read_MultipleLines_TracksLineNumbers()
    {
        // Act
        var records = CsvReader.Read("h1,h2\nr1,r2\nr3,r4", ',');

        // Assert
        Assert.Equal([1, 2, 3], records.Select(r => r.LineNumber));
    }
}
