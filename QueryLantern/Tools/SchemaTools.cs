namespace QueryLantern.Tools;

using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Schema;

/// <summary>
/// Schema aware tools the agent uses to explore the connected database: list tables, describe a
/// table, sample rows, and show an execution plan. All are read only and auditable.
/// </summary>
public sealed class SchemaTools
{
    private readonly IDatabaseAdapter _adapter;
    private readonly SchemaCache _cache;
    private readonly int _sampleCap;

    public SchemaTools(IDatabaseAdapter adapter, SchemaCache cache, int sampleCap = 50)
    {
        _adapter = adapter;
        _cache = cache;
        _sampleCap = sampleCap;
    }

    [Tool("List the tables available in the connected database")]
    public string ListTables([ToolInput("Optional schema or catalog name to filter by")] string schema = "")
    {
        var model = _cache.Get(0) ?? _adapter.IntrospectSchemaAsync().GetAwaiter().GetResult();
        var tables = string.IsNullOrWhiteSpace(schema)
            ? model.Tables
            : model.Tables.Where(t => t.Schema == schema).ToList();
        return JsonSerializer.Serialize(new { tables = tables.Select(t => new { t.Schema, t.Name, Columns = t.Columns.Count }).ToArray() });
    }

    [Tool("Describe a table: its columns, types, and nullability")]
    public string DescribeTable([ToolInput("The table name, optionally schema-qualified as schema.name")] string table)
    {
        var model = _cache.Get(0) ?? _adapter.IntrospectSchemaAsync().GetAwaiter().GetResult();
        var (schema, name) = SplitName(table, model);
        var found = model.Tables.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(schema) || string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase)));
        if (found is null)
        {
            return JsonSerializer.Serialize(new { error = $"Table '{table}' not found." });
        }

        return JsonSerializer.Serialize(new
        {
            found.Schema,
            found.Name,
            columns = found.Columns.Select(c => new { c.Name, c.DataType, c.IsNullable }).ToArray()
        });
    }

    [Tool("Return a small sample of rows from a table for quick preview")]
    public string SampleRows([ToolInput("The table name, optionally schema-qualified")] string table, [ToolInput("Maximum number of rows to return")] int limit = 10)
    {
        var effectiveLimit = Math.Clamp(limit, 1, _sampleCap);
        var sql = $"SELECT * FROM {Quote(table)} LIMIT {effectiveLimit}";
        var result = _adapter.ExecuteReadAsync(sql, maxRows: effectiveLimit).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(new
        {
            columns = result.Columns.Select(c => new { c.Name, c.DataType }).ToArray(),
            rows = result.Rows.Select(r => r.ToArray()).ToArray(),
            rowCount = result.RowCount
        });
    }

    [Tool("Return the database engine execution plan for a SELECT statement")]
    public string ExplainPlan([ToolInput("A single read-only SELECT statement")] string sql)
    {
        var plan = _adapter.ExecuteReadAsync("EXPLAIN " + sql, maxRows: 200).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(new
        {
            columns = plan.Columns.Select(c => new { c.Name, c.DataType }).ToArray(),
            rows = plan.Rows.Select(r => r.ToArray()).ToArray()
        });
    }

    private static (string Schema, string Name) SplitName(string table, Adapters.SchemaModel model)
    {
        var dot = table.IndexOf('.');
        if (dot < 0)
        {
            return (string.Empty, table);
        }

        var schema = table[..dot];
        var name = table[(dot + 1)..];
        // If the prefix is not a known schema, treat the whole thing as a table name.
        if (model.Tables.Any(t => string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase)))
        {
            return (schema, name);
        }

        return (string.Empty, table);
    }

    private static string Quote(string table)
    {
        var dot = table.IndexOf('.');
        var (schema, name) = dot < 0 ? (string.Empty, table) : (table[..dot], table[(dot + 1)..]);
        return string.IsNullOrEmpty(schema) ? $"\"{name}\"" : $"\"{schema}\".\"{name}\"";
    }
}
