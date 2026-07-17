namespace QueryLantern.Tools;

using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;

/// <summary>
/// The governed read only query tool the agent calls to read data. It executes a single read only
/// statement against the active database adapter, parameterised and row capped, and returns a JSON
/// ResultSet the UI can grid and chart. Non read statements are rejected at the tool boundary.
/// </summary>
public sealed class QueryTools
{
    private readonly IDatabaseAdapter _adapter;
    private readonly int _maxRows;
    private readonly SqlSafetyClassifier _safety = new();

    public QueryTools(IDatabaseAdapter adapter, int maxRows = 1000)
    {
        _adapter = adapter;
        _maxRows = maxRows;
    }

    [Tool("Execute a read-only SQL query against the active connection and return the rows")]
    public string RunQuery([ToolInput("A single read-only SQL SELECT statement")] string sql)
    {
        var classification = _safety.Classify(sql);
        if (classification.Kind != StatementKind.Read)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Only read-only statements are allowed.",
                rejected = classification.Kind.ToString(),
                detail = classification.Reason
            });
        }

        var result = _adapter.ExecuteReadAsync(sql, maxRows: _maxRows).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(new
        {
            columns = result.Columns.Select(c => new { c.Name, c.DataType }).ToArray(),
            rows = result.Rows.Select(r => r.ToArray()).ToArray(),
            rowCount = result.RowCount,
            truncatedAt = result.TruncatedAt
        });
    }
}
