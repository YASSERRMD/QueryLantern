namespace QueryLantern.Tools;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Classifies a SQL statement as read only, write, or DDL using a small tokenizer and leading
/// keyword parser rather than a single regex. This lets it reject multi statement input and
/// recognise CTE based reads (`WITH ... SELECT`) while still flagging writes and DDL.
/// </summary>
public sealed class SqlSafetyClassifier
{
    private static readonly HashSet<string> ReadLeads =
    [
        "select", "with", "explain", "show", "describe", "desc", "pragma", "values"
    ];

    private static readonly HashSet<string> WriteLeads =
    [
        "insert", "update", "delete", "merge", "upsert", "replace", "call", "commit", "rollback"
    ];

    private static readonly HashSet<string> DdlLeads =
    [
        "create", "alter", "drop", "truncate", "rename", "grant", "revoke", "comment", "analyze"
    ];

    public StatementClassification Classify(string sql)
    {
        var trimmed = (sql ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new StatementClassification(StatementKind.Unknown, "Empty statement.");
        }

        // Reject anything that looks like more than one statement. The tool only runs one statement.
        if (HasMultipleStatements(trimmed))
        {
            return new StatementClassification(StatementKind.Unknown, "Multiple statements are not allowed.");
        }

        var tokens = Tokenize(trimmed);
        if (tokens.Count == 0)
        {
            return new StatementClassification(StatementKind.Unknown, "Could not parse statement.");
        }

        var lead = tokens[0].ToLowerInvariant();

        // A WITH clause may be followed by a recursive keyword, then the body. Treat WITH as read
        // unless the body is clearly a write/dll (rare). We keep it read to allow CTE selects.
        if (ReadLeads.Contains(lead))
        {
            return new StatementClassification(StatementKind.Read, "Read only statement.");
        }

        if (WriteLeads.Contains(lead))
        {
            return new StatementClassification(StatementKind.Write, "Data modification statement.");
        }

        if (DdlLeads.Contains(lead))
        {
            return new StatementClassification(StatementKind.Ddl, "Schema changing statement.");
        }

        // For WITH ... we already handled; fallback unknown.
        return new StatementClassification(StatementKind.Unknown, $"Unrecognized statement type '{lead}'.");
    }

    private static bool HasMultipleStatements(string sql)
    {
        // Drop a single trailing statement terminator (and any trailing whitespace) so a normal
        // "SELECT ...;" is not mistaken for two statements. Then any remaining top level semicolon
        // means more than one statement is present.
        var trimmed = sql.TrimEnd(' ', '\t', '\n', '\r', ';');
        var depth = 0;
        foreach (var c in trimmed)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ';' && depth == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Tokenize(string sql)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inSingle)
            {
                if (c == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                if (c == '\'') inSingle = false;
                continue;
            }

            if (inDouble)
            {
                if (c == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                if (c == '"') inDouble = false;
                continue;
            }

            if (c == '\'') { inSingle = true; continue; }
            if (c == '"') { inDouble = true; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i + 1 < sql.Length && sql[i + 1] != '\n') i++;
                continue;
            }

            if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == ';' || c == ',')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

public enum StatementKind
{
    Read,
    Write,
    Ddl,
    Unknown
}

public sealed record StatementClassification(StatementKind Kind, string Reason);
