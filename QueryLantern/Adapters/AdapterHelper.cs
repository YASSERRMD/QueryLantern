namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Shared helpers for <see cref="IDatabaseAdapter"/> implementations so each engine only supplies
/// connection string construction, parameter binding, and introspection SQL.
/// </summary>
internal static class AdapterHelper
{
    public static async Task<QueryResult> ReadAsync(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new List<ColumnMeta>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ColumnMeta(reader.GetName(i), reader.GetFieldType(i).Name));
        }

        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            TruncatedAt = truncated ? maxRows : null
        };
    }

    public static void ApplyParameters(DbCommand cmd, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var (key, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = key.StartsWith("@", StringComparison.Ordinal) ? key : "@" + key;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}
