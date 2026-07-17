namespace QueryLantern.Tools;

using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;

/// <summary>
/// The write staging tool. When the agent wants to run a mutating statement it calls propose_write,
/// which does NOT execute. The tool is registered with Ancora as requiring approval, so the run
/// suspends and waits for a human decision. After approval, the resume flow executes the staged
/// statement through the adapter and continues the run.
/// </summary>
public sealed class WriteTools
{
    private readonly IDatabaseAdapter _adapter;

    public WriteTools(IDatabaseAdapter adapter)
    {
        _adapter = adapter;
    }

    [Tool("Stage a mutating SQL statement for human approval. Does not execute until approved.")]
    public string ProposeWrite([ToolInput("A single mutating SQL statement (INSERT, UPDATE, DELETE, or DDL)")] string sql)
    {
        var classification = new SqlSafetyClassifier().Classify(sql);
        var affected = classification.Kind == StatementKind.Write ? "data" : classification.Kind == StatementKind.Ddl ? "schema" : "unknown";
        return JsonSerializer.Serialize(new
        {
            staged = true,
            sql,
            kind = classification.Kind.ToString(),
            affects = affected,
            reason = classification.Reason
        });
    }

    /// <summary>
    /// Executes a previously staged, approved statement. Only called by the resume flow after an
    /// explicit approval decision.
    /// </summary>
    public int ExecuteApproved(string sql)
    {
        return _adapter.ExecuteWriteAsync(sql).GetAwaiter().GetResult();
    }
}
