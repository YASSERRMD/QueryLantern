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
    /// Registers the governed tools on the runtime for the given adapter and returns their specs so
    /// the caller can embed them in the agent spec. Read tools run immediately; write tools stage for
    /// human approval (the run suspends until a decision resumes it).
    /// </summary>
    public List<ToolSpec> RegisterTools(Runtime runtime, IDatabaseAdapter adapter, int maxRows = 1000)
    {
        var specs = new List<ToolSpec>();
        var queryTools = new QueryTools(adapter, maxRows);
        var writeTools = new WriteTools(adapter);

        var sqlInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single SQL statement") },
            new List<string> { "sql" });

        ToolRegistry.Register(runtime, "run_query", "Execute a read-only SQL query against the active connection and return the rows", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            return queryTools.RunQuery(sql);
        });
        specs.Add(new ToolSpec("run_query", "Execute a read-only SQL query against the active connection and return the rows", sqlInput));

        // propose_write requires approval: Ancora suspends the run when this tool is called.
        ToolRegistry.RegisterRequiringApproval(runtime, "propose_write", "Stage a mutating SQL statement for human approval. Does not execute until approved.", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            return writeTools.ProposeWrite(sql);
        });
        specs.Add(new ToolSpec("propose_write", "Stage a mutating SQL statement for human approval. Does not execute until approved.", sqlInput));

        return specs;
    }

    /// <summary>
    /// Registers only the read tool (used before the write tool is needed).
    /// </summary>
    public List<ToolSpec> RegisterReadTools(Runtime runtime, IDatabaseAdapter adapter, int maxRows = 1000)
        => RegisterTools(runtime, adapter, maxRows);

    /// <summary>
    /// Executes a staged, approved write statement via the write tools bound to the adapter.
    /// </summary>
    public int ExecuteApprovedWrite(IDatabaseAdapter adapter, string sql)
        => new WriteTools(adapter).ExecuteApproved(sql);
}
