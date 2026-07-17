namespace QueryLantern.Tools;

using System.Collections.Generic;
using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Schema;

/// <summary>
/// Builds and registers the governed tools (run_query, the schema aware tools, and the write staging
/// tool) on an Ancora runtime for a specific connection, and returns the matching tool specs to embed
/// in the agent spec. Tools are individual and auditable through the Ancora tool registry.
/// </summary>
public sealed class AgentToolbox
{
    /// <summary>
    /// The set of tool names this toolbox can register. Used by the plan validator to confirm a plan
    /// references only real tools.
    /// </summary>
    public static IReadOnlySet<string> KnownToolNames { get; } = new HashSet<string>(System.StringComparer.Ordinal)
    {
        "run_query",
        "list_tables",
        "describe_table",
        "sample_rows",
        "explain_plan",
        "planner_plan",
        "propose_write"
    };

    private readonly SchemaCache _schemaCache;

    public AgentToolbox(SchemaCache schemaCache)
    {
        _schemaCache = schemaCache;
    }

    /// <summary>
    /// Registers the governed tools on the runtime for the given adapter and returns their specs so
    /// the caller can embed them in the agent spec. Read tools run immediately; write tools stage for
    /// human approval (the run suspends until a decision resumes it).
    /// </summary>
    public List<ToolSpec> RegisterTools(Runtime runtime, IDatabaseAdapter adapter, int maxRows = 1000, Action<string>? onRunQueryResult = null)
    {
        var specs = RegisterReadHandlers(runtime, adapter, maxRows, onRunQueryResult);
        var writeTools = new WriteTools(adapter);
        var sqlInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single SQL statement") },
            new List<string> { "sql" });
        ToolRegistry.RegisterRequiringApproval(runtime, "propose_write", "Stage a mutating SQL statement for human approval. Does not execute until approved.", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            return writeTools.ProposeWrite(sql);
        });
        specs.Add(new ToolSpec("propose_write", "Stage a mutating SQL statement for human approval. Does not execute until approved.", sqlInput));
        return specs;
    }

    /// <summary>
    /// Registers the read-only governed tool handlers on a runtime (list_tables, describe_table,
    /// sample_rows, explain_plan, run_query, planner_plan) and returns their specs. Shared by the
    /// single-agent run and the multi-step graph run so every node can call any read tool.
    /// </summary>
    public List<ToolSpec> RegisterReadHandlers(Runtime runtime, IDatabaseAdapter adapter, int maxRows = 1000, Action<string>? onRunQueryResult = null)
    {
        var specs = new List<ToolSpec>();
        var queryTools = new QueryTools(adapter, maxRows);
        var schemaTools = new SchemaTools(adapter, _schemaCache);
        var plannerTools = new PlannerTool(_schemaCache);

        var sqlInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single SQL statement") },
            new List<string> { "sql" });

        ToolRegistry.Register(runtime, "run_query", "Execute a read-only SQL query against the active connection and return the rows", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            var result = queryTools.RunQuery(sql);
            onRunQueryResult?.Invoke(result);
            return result;
        });
        specs.Add(new ToolSpec("run_query", "Execute a read-only SQL query against the active connection and return the rows", sqlInput));

        var tableInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["table"] = new("string", "A table name, optionally schema-qualified as schema.name") },
            new List<string> { "table" });
        var schemaFilterInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["schema"] = new("string", "Optional schema or catalog name to filter by") },
            new List<string>());
        var sampleInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty>
            {
                ["table"] = new("string", "A table name, optionally schema-qualified"),
                ["limit"] = new("integer", "Maximum number of rows to return")
            },
            new List<string> { "table" });
        var planInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single read-only SELECT statement") },
            new List<string> { "sql" });

        ToolRegistry.Register(runtime, "list_tables", "List the tables available in the connected database", input =>
        {
            var schema = input.TryGetProperty("schema", out var sEl) ? sEl.GetString() ?? string.Empty : string.Empty;
            return schemaTools.ListTables(schema);
        });
        specs.Add(new ToolSpec("list_tables", "List the tables available in the connected database", schemaFilterInput));

        ToolRegistry.Register(runtime, "describe_table", "Describe a table: its columns, types, and nullability", input =>
        {
            var table = input.TryGetProperty("table", out var tEl) ? tEl.GetString() ?? string.Empty : string.Empty;
            return schemaTools.DescribeTable(table);
        });
        specs.Add(new ToolSpec("describe_table", "Describe a table: its columns, types, and nullability", tableInput));

        ToolRegistry.Register(runtime, "sample_rows", "Return a small sample of rows from a table for quick preview", input =>
        {
            var table = input.TryGetProperty("table", out var tEl) ? tEl.GetString() ?? string.Empty : string.Empty;
            var limit = input.TryGetProperty("limit", out var lEl) ? lEl.GetInt32() : 10;
            return schemaTools.SampleRows(table, limit);
        });
        specs.Add(new ToolSpec("sample_rows", "Return a small sample of rows from a table for quick preview", sampleInput));

        ToolRegistry.Register(runtime, "explain_plan", "Return the database engine execution plan for a SELECT statement", input =>
        {
            var sql = input.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() ?? string.Empty : string.Empty;
            return schemaTools.ExplainPlan(sql);
        });
        specs.Add(new ToolSpec("explain_plan", "Return the database engine execution plan for a SELECT statement", planInput));

        var plannerInput = new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty>
            {
                ["question"] = new("string", "The user question to plan for"),
                ["schemaSummary"] = new("string", "Optional compact schema summary to ground the plan")
            },
            new List<string> { "question" });
        ToolRegistry.Register(runtime, "planner_plan", "Produce an explicit, inspectable PlanGraph for a user question against the schema", input =>
        {
            var question = input.TryGetProperty("question", out var qEl) ? qEl.GetString() ?? string.Empty : string.Empty;
            var summary = input.TryGetProperty("schemaSummary", out var sEl) ? sEl.GetString() : null;
            return plannerTools.Plan(question, summary);
        });
        specs.Add(new ToolSpec("planner_plan", "Produce an explicit, inspectable PlanGraph for a user question against the schema", plannerInput));

        return specs;
    }

    /// <summary>
    /// Builds the read-only tool specs (list_tables, describe_table, sample_rows, explain_plan,
    /// run_query, planner_plan) without registering handlers on a runtime. Used to embed governed tools
    /// in an AgentSpec for a graph node.
    /// </summary>
    public List<ToolSpec> BuildReadToolSpecs()
    {
        var specs = new List<ToolSpec>();
        specs.Add(new ToolSpec("run_query", "Execute a read-only SQL query against the active connection and return the rows", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single SQL statement") },
            new List<string> { "sql" })));
        specs.Add(new ToolSpec("list_tables", "List the tables available in the connected database", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["schema"] = new("string", "Optional schema or catalog name to filter by") },
            new List<string>())));
        specs.Add(new ToolSpec("describe_table", "Describe a table: its columns, types, and nullability", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["table"] = new("string", "A table name, optionally schema-qualified as schema.name") },
            new List<string> { "table" })));
        specs.Add(new ToolSpec("sample_rows", "Return a small sample of rows from a table for quick preview", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty>
            {
                ["table"] = new("string", "A table name, optionally schema-qualified"),
                ["limit"] = new("integer", "Maximum number of rows to return")
            },
            new List<string> { "table" })));
        specs.Add(new ToolSpec("explain_plan", "Return the database engine execution plan for a SELECT statement", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty> { ["sql"] = new("string", "A single read-only SELECT statement") },
            new List<string> { "sql" })));
        specs.Add(new ToolSpec("planner_plan", "Produce an explicit, inspectable PlanGraph for a user question against the schema", new ToolInputSchema(
            "object",
            new Dictionary<string, ToolInputProperty>
            {
                ["question"] = new("string", "The user question to plan for"),
                ["schemaSummary"] = new("string", "Optional compact schema summary to ground the plan")
            },
            new List<string> { "question" })));
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
