namespace QueryLantern.Adapters;

using System.Collections.Generic;

/// <summary>
/// A normalized description of a database schema the agent can reason over. Populated per adapter
/// from engine specific introspection and cached per connection (see Phase 8).
/// </summary>
public sealed class SchemaModel
{
    public string Engine { get; init; } = string.Empty;
    public IReadOnlyList<TableModel> Tables { get; init; } = new List<TableModel>();
}

/// <summary>
/// A table with its columns and key information. Collections are mutable so adapters can populate
/// them during introspection.
/// </summary>
public sealed record TableModel(
    string Name,
    string Schema,
    List<ColumnModel> Columns,
    List<string> PrimaryKey);

/// <summary>
/// A column with its type and nullability.
/// </summary>
public sealed record ColumnModel(string Name, string DataType, bool IsNullable);
