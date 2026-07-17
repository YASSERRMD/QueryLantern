namespace QueryLantern.Models;

/// <summary>
/// Kind of a learned schema fact.
/// </summary>
public enum SchemaFactKind
{
    Quirk = 0,
    Synonym = 1,
    Convention = 2,
    Warning = 3
}

/// <summary>
/// A durable fact learned about a connection's schema, persisted so future analyses start from prior
/// knowledge instead of rediscovering it. Only structural/semantic facts are stored, never row data.
/// </summary>
public sealed record SchemaFact(int Id, int ConnectionId, string Fact, SchemaFactKind Kind, DateTime CreatedAt);
