namespace QueryLantern.Tools;

using System.Text;
using System.Text.Json;

/// <summary>
/// Converts a run_query result-set JSON (columns + rows) into CSV for export.
/// </summary>
public static class CsvExporter
{
    public static string ToCsv(string resultJson)
    {
        var sb = new StringBuilder();
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("columns", out var cols) || !root.TryGetProperty("rows", out var rowsEl))
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var c in cols.EnumerateArray())
        {
            names.Add(c.GetProperty("name").GetString() ?? string.Empty);
        }
        sb.AppendLine(string.Join(",", names.Select(Escape)));

        foreach (var row in rowsEl.EnumerateArray())
        {
            var cells = new List<string>();
            foreach (var cell in row.EnumerateArray())
            {
                cells.Add(cell.ValueKind == JsonValueKind.Null ? string.Empty : Escape(cell.ToString()));
            }
            sb.AppendLine(string.Join(",", cells));
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
