namespace QueryLantern.Schema;

using System.Text;
using QueryLantern.Adapters;

/// <summary>
/// Produces a compact, token efficient description of a schema that the agent uses to reason about
/// tables and columns. The format is stable so cached prompts do not churn.
/// </summary>
public static class SchemaSerializer
{
    /// <summary>
    /// Renders the schema as a compact multi line text block, one table per section.
    /// </summary>
    public static string ToCompactText(SchemaModel schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"engine: {schema.Engine}");
        sb.AppendLine($"tables: {schema.Tables.Count}");
        foreach (var table in schema.Tables)
        {
            var scope = table.Schema == "main" || table.Schema == "default"
                ? table.Name
                : $"{table.Schema}.{table.Name}";
            sb.AppendLine($"- {scope} ({table.Columns.Count} cols)");
            foreach (var column in table.Columns)
            {
                var flags = column.IsNullable ? "null" : "not null";
                sb.AppendLine($"    {column.Name}: {column.DataType} [{flags}]");
            }
        }

        return sb.ToString();
    }
}
