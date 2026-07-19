using System.Text;

namespace CrmApi.Services;

// Counterpart to CsvParser — RFC 4180-ish: quote a field only when it
// contains a comma, quote, or newline, doubling any embedded quotes.
public static class CsvWriter
{
    public static string Write(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }
        return sb.ToString();
    }

    private static string Escape(string? field)
    {
        field ??= "";
        return field.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }
}
