namespace QueryLantern.Adapters;

using System.Collections.Generic;

/// <summary>
/// Metadata describing a single result column.
/// </summary>
public sealed record ColumnMeta(string Name, string DataType);

/// <summary>
/// A typed result set returned by read queries. Rows are lists of object values aligned to
/// <see cref="Columns"/>. Nulls are preserved as null.
/// </summary>
public sealed class QueryResult
{
    public IReadOnlyList<ColumnMeta> Columns { get; init; } = new List<ColumnMeta>();
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = new List<IReadOnlyList<object?>>();
    public int RowCount => Rows.Count;
    public int? TruncatedAt { get; init; }
}

/// <summary>
/// The outcome of a connection test.
/// </summary>
public sealed record TestResult(bool Success, string Message);
