namespace QueryLantern.Tools;

/// <summary>
/// Classifies a SQL statement as read only, write, or DDL. Phase 11 uses a statement lead token
/// check; Phase 12 replaces this with a fuller parser based classifier. Keeping the type here lets
/// the query tool reject non read statements at the tool boundary from the start.
/// </summary>
public sealed class SqlSafetyClassifier
{
    public StatementClassification Classify(string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';', ' ', '\n', '\r');
        if (trimmed.Length == 0)
        {
            return new StatementClassification(StatementKind.Unknown, "Empty statement.");
        }

        var lead = LeadingToken(trimmed);
        return lead switch
        {
            "select" or "with" or "explain" or "show" or "describe" or "desc" or "pragma"
                => new StatementClassification(StatementKind.Read, "Read only statement."),
            "insert" or "update" or "delete" or "merge" or "upsert"
                => new StatementClassification(StatementKind.Write, "Data modification statement."),
            "create" or "alter" or "drop" or "truncate" or "rename" or "grant" or "revoke"
                => new StatementClassification(StatementKind.Ddl, "Schema changing statement."),
            _ => new StatementClassification(StatementKind.Unknown, $"Unrecognized statement type '{lead}'.")
        };
    }

    private static string LeadingToken(string sql)
    {
        var space = sql.IndexOf(' ');
        var token = space < 0 ? sql : sql[..space];
        return token.ToLowerInvariant();
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
