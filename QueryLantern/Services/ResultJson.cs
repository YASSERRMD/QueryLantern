namespace QueryLantern.Services;

using System.Text.Json;
using QueryLantern.Adapters;

/// <summary>
/// Serializes a QueryResult into the JSON shape the UI ResultGrid expects (camelCase columns/rows).
/// Centralized so services and pages produce identical output.
/// </summary>
public static class ResultJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(QueryResult result) => JsonSerializer.Serialize(new
    {
        columns = result.Columns.Select(c => new { c.Name, c.DataType }),
        rows = result.Rows,
        rowCount = result.RowCount,
        truncatedAt = result.TruncatedAt
    }, Options);
}
