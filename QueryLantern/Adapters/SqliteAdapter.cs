namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// SQLite adapter. Uses a file or in memory database and implements the full
/// <see cref="IDatabaseAdapter"/> contract including schema introspection.
/// </summary>
public sealed class SqliteAdapter : IDatabaseAdapter, IDisposable
{
    private SqliteConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.Sqlite;

    public async Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        var dataSource = profile.Host.Length == 0 ? profile.Database : profile.Host;
        if (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            _connection = new SqliteConnection("Data Source=:memory:");
        }
        else
        {
            _connection = new SqliteConnection($"Data Source={dataSource}");
        }

        await _connection.OpenAsync(ct);
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        _connection?.Close();
        return Task.CompletedTask;
    }

    public async Task<TestResult> TestConnectionAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection($"Data Source={profile.Database}");
            await conn.OpenAsync(ct);
            return new TestResult(true, "Connection succeeded.");
        }
        catch (Exception ex)
        {
            return new TestResult(false, ex.Message);
        }
    }

    public async Task<QueryResult> ExecuteReadAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int maxRows = 1000,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        ApplyParameters(cmd, parameters);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
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

    public async Task<int> ExecuteWriteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        ApplyParameters(cmd, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SchemaModel> IntrospectSchemaAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);

        var tableNames = new List<string>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        var tables = new List<TableModel>();
        foreach (var table in tableNames)
        {
            var columns = new List<ColumnModel>();
            await using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}');";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var name = reader.GetString(1);
                    var type = reader.GetString(2);
                    var notNull = reader.GetInt32(3) == 1;
                    columns.Add(new ColumnModel(name, type, !notNull));
                }
            }

            var pk = new List<string>();
            tables.Add(new TableModel(table, "main", columns, pk));
        }

        return new SchemaModel { Engine = "sqlite", Tables = tables };
    }

    private async Task EnsureOpen(CancellationToken ct)
    {
        if (_connection is null)
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            await _connection.OpenAsync(ct);
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct);
        }
    }

    private static void ApplyParameters(SqliteCommand cmd, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.Add(new SqliteParameter(key.StartsWith("@", StringComparison.Ordinal) ? key : "@" + key, value ?? DBNull.Value));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
    }
}
