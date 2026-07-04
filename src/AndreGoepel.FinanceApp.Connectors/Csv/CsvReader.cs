namespace AndreGoepel.FinanceApp.Connectors.Csv;

/// <summary>One physical CSV record with its 1-based starting line number and raw text.</summary>
internal sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields, string RawLine);

/// <summary>
/// Minimal RFC 4180 reader: quoted fields, escaped quotes (""), embedded
/// delimiters and line breaks inside quotes. No external dependency — provider
/// exports are small and simple.
/// </summary>
internal static class CsvReader
{
    public static List<CsvRecord> Read(string content, char delimiter)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var raw = new System.Text.StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordStartLine = 1;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            // A record consisting of a single empty field is a blank line — skip.
            if (fields.Count > 1 || fields[0].Length > 0)
            {
                records.Add(new CsvRecord(recordStartLine, fields.ToArray(), raw.ToString()));
            }
            fields.Clear();
            raw.Clear();
            recordStartLine = line;
        }

        for (var i = 0; i < content.Length; i++)
        {
            var character = content[i];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        raw.Append("\"\"");
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                        raw.Append(character);
                    }
                }
                else
                {
                    if (character == '\n')
                    {
                        line++;
                    }
                    field.Append(character);
                    raw.Append(character);
                }
                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
                raw.Append(character);
            }
            else if (character == delimiter)
            {
                EndField();
                raw.Append(character);
            }
            else if (character == '\r')
            {
                // Handled together with the following \n (or treated as \n alone).
                if (i + 1 >= content.Length || content[i + 1] != '\n')
                {
                    line++;
                    EndRecord();
                }
            }
            else if (character == '\n')
            {
                line++;
                EndRecord();
            }
            else
            {
                field.Append(character);
                raw.Append(character);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}
