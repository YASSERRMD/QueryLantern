namespace QueryLantern.Tools;

using System.Collections.Generic;
using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;

/// <summary>
/// Builds and registers the governed tools (run_query and the schema aware tools) on an Ancora
/// runtime for a specific connection, and returns the matching tool specs to embed in the agent
/// spec. Tools are individual and auditable through the Ancora tool registry.
/// </summary>
public sealed class AgentToolbox
{
    /// <summary>
    /// Registers run_query (read only) on the runtime for the given adapter and returns its spec.
    /// The schema aware tools are added in Phase 13.
    /// </summary>
    public List<ToolSpec> RegisterReadTools(Runtime runtime, IDatabaseAdapter adapter, int maxRows = 1000)
    {
        var specs = new List<ToolSpec>();
        var queryTools = new QueryTools(adapter, maxRows);

        var inputSchema = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single read-only SQL SELECT statement") },
            new List<string> { "sql" });

        ToolRegistry.Register(runtime, "run_query", "Execute a read-only SQL query against the active connection and return the rows", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            return queryTools.RunQuery(sql);
        });

        specs.Add(new ToolSpec("run_query", "Execute a read-only SQL query against the active connection and return the rows", inputSchema));
        return specs;
    }
}
