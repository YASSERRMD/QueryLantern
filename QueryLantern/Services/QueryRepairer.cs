namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QueryLantern.Adapters;
using QueryLantern.Models;

/// <summary>
/// Tracks correction attempts for a step and enforces a bounded retry budget so the agent cannot loop
/// forever repairing a failing query.
/// </summary>
public sealed class CorrectionBudget
{
    private int _attempts;

    public CorrectionBudget(int maxAttempts = 3)
    {
        MaxAttempts = maxAttempts;
    }

    public int MaxAttempts { get; }
    public int Attempts => _attempts;
    public bool Exhausted => _attempts >= MaxAttempts;

    /// <summary>
    /// Records one correction attempt. Returns false (and marks exhausted) once the budget is reached.
    /// </summary>
    public bool TryRecord()
    {
        if (Exhausted) return false;
        _attempts++;
        return true;
    }
}

/// <summary>
/// Produces a corrected SQL statement from a failing query, the exact engine error, and the schema. The
/// repair is schema-aware and deterministic (no model call) so it is testable and auditable: it fixes
/// unknown columns by matching a known column on the referenced table, and fixes unknown tables
/// similarly. When it cannot infer a fix, it returns the original SQL unchanged and the caller must ask
/// the user.
/// </summary>
public sealed class QueryRepairer
{
    private readonly SchemaModel _schema;

    public QueryRepairer(SchemaModel schema)
    {
        _schema = schema;
    }

    /// <summary>
    /// Returns a corrected SQL string for the given failing SQL and error. The error text is scanned for
    /// the offending identifier; if it matches a known column or table (by name or fuzzy prefix), the
    /// SQL is rewritten to use the correct identifier.
    /// </summary>
    public string Repair(string sql, string error)
    {
        // Extract an identifier the engine complained about: "no such column: X" or "no such table: Y".
        var columnMatch = Regex.Match(error, @"no such column:?\s*([\w.]+)", RegexOptions.IgnoreCase);
        if (columnMatch.Success)
        {
            var bad = columnMatch.Groups[1].Value;
            var fixedName = FindSimilarColumn(bad);
            if (fixedName is not null)
            {
                return ReplaceIdentifier(sql, bad, fixedName);
            }
        }

        var tableMatch = Regex.Match(error, @"no such table:?\s*([\w.]+)", RegexOptions.IgnoreCase);
        if (tableMatch.Success)
        {
            var bad = tableMatch.Groups[1].Value;
            var fixedName = FindSimilarTable(bad);
            if (fixedName is not null)
            {
                return ReplaceIdentifier(sql, bad, fixedName);
            }
        }

        return sql;
    }

    private string? FindSimilarColumn(string bad)
    {
        var bare = bad.Contains('.') ? bad[(bad.IndexOf('.') + 1)..] : bad;
        foreach (var table in _schema.Tables)
        {
            var exact = table.Columns.FirstOrDefault(c => string.Equals(c.Name, bare, System.StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return table.Schema == "main" || table.Schema == "default"
                    ? exact.Name
                    : $"{table.Name}.{exact.Name}";
            }
        }

        // Fuzzy: a column whose name contains the bad token, or shares a significant prefix (for
        // example "cust_id" matching "customer_id"). The reverse (bad contains column) is intentionally
        // avoided because short column names like "id" would otherwise wrongly match.
        var prefix = bare.Length >= 4 ? bare[..4] : bare;
        foreach (var table in _schema.Tables)
        {
            var fuzzy = table.Columns.FirstOrDefault(c =>
                c.Name.Contains(bare, System.StringComparison.OrdinalIgnoreCase) ||
                c.Name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase));
            if (fuzzy is not null)
            {
                return table.Schema == "main" || table.Schema == "default"
                    ? fuzzy.Name
                    : $"{table.Name}.{fuzzy.Name}";
            }
        }

        return null;
    }

    private string? FindSimilarTable(string bad)
    {
        var exact = _schema.Tables.FirstOrDefault(t => string.Equals(t.Name, bad, System.StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Name;
        var prefix = bad.Length >= 4 ? bad[..4] : bad;
        var fuzzy = _schema.Tables.FirstOrDefault(t =>
            t.Name.Contains(bad, System.StringComparison.OrdinalIgnoreCase) ||
            t.Name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase));
        return fuzzy?.Name;
    }

    private static string ReplaceIdentifier(string sql, string bad, string good)
    {
        // Replace the bare bad identifier (word-bounded) everywhere it appears.
        return Regex.Replace(sql, $@"\b{Regex.Escape(bad)}\b", good);
    }
}
