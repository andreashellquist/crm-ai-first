namespace CrmApi.Services;

// A minimal RFC 4180 CSV parser — no external dependency for what's a small,
// well-defined format. Handles quoted fields, embedded commas/newlines inside
// quotes, and "" as an escaped quote. Deliberately lenient rather than
// strict: a stray quote outside a quoted field is treated as a literal
// character rather than a parse error, since real-world exported CSVs are
// not always perfectly RFC-compliant and this is import tooling, not a
// validator.
public static class CsvParser
{
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            rows.Add(row.ToArray());
            row = [];
            rowHasContent = false;
        }

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                rowHasContent = true;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    EndField();
                    rowHasContent = true;
                    break;
                case '\r':
                    break; // swallow; '\n' (bare or following) ends the row
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    rowHasContent = true;
                    break;
            }
        }

        // Flush a trailing row that wasn't newline-terminated (common for the
        // last line of a file) — but not a phantom empty row from a trailing
        // blank line.
        if (rowHasContent || field.Length > 0)
        {
            EndRow();
        }

        return rows;
    }
}
