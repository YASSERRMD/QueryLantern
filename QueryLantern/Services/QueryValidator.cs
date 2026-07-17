namespace QueryLantern.Services;

using System.Text.Json;
using QueryLantern.Adapters;

/// <summary>
/// The outcome of dry-running a SELECT before execution: whether it is valid, and the structured error
/// text when it is not. The agent receives this as feedback and must not execute a rejected query.
/// </summary>
public sealed record ValidationResult(bool IsValid, string? Error, string Engine);

/// <summary>
/// Validates a generated SELECT before it executes, by compiling it through the engine (EXPLAIN or
/// prepare) without running it. A query that the engine rejects is reported as invalid with the
/// engine's error text so the agent can repair it. Read-only by construction: dry-run never returns
/// rows.
/// </summary>
public sealed class QueryValidator
{
    private readonly IDatabaseAdapter _adapter;

    public QueryValidator(IDatabaseAdapter adapter)
    {
        _adapter = adapter;
    }

    /// <summary>
    /// Dry-runs the SELECT. Returns a valid result when the engine compiles it; otherwise returns the
    /// engine error as structured feedback. Never executes the query.
    /// </summary>
    public ValidationResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ValidationResult(false, "Empty SQL.", _adapter.Engine.ToString());
        }

        try
        {
            // EXPLAIN compiles the statement without executing it. If the engine rejects it (bad column,
            // bad syntax), this throws and we surface the error.
            var plan = _adapter.ExecuteReadAsync("EXPLAIN " + sql, maxRows: 1).GetAwaiter().GetResult();
            _ = plan.RowCount;
            return new ValidationResult(true, null, _adapter.Engine.ToString());
        }
        catch (System.Exception ex)
        {
            return new ValidationResult(false, ex.Message, _adapter.Engine.ToString());
        }
    }

    /// <summary>
    /// Serializes the validation result as JSON so it can be returned to the agent as structured
    /// feedback.
    /// </summary>
    public string ToFeedback(string sql)
    {
        var result = Validate(sql);
        return JsonSerializer.Serialize(new
        {
            sql,
            valid = result.IsValid,
            error = result.Error
        });
    }
}
