namespace QueryLantern.Models;

/// <summary>
/// Kind of a semantic-layer term.
/// </summary>
public enum SemanticTermKind
{
    Metric = 0,
    Dimension = 1,
    Synonym = 2
}

/// <summary>
/// A business term mapped to a schema element so the model can translate natural language into the
/// right column or expression. Only the mapping metadata is stored, never row data.
/// </summary>
public sealed record SemanticTerm(int Id, int ConnectionId, string Term, SemanticTermKind Kind, string Expression, string? Table);
