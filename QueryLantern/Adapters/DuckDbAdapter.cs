namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using QueryLantern.Models;

/// <summary>
/// DuckDB adapter for in process analytics over local files (parquet, csv) and the full DuckDB
/// SQL dialect. Implements the full <see cref="IDatabaseAdapter"/> contract.
/// </summary>
public sealed class DuckDbAdapter : IDatabaseAdapter, IDisposable
{
    private DuckDBConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.DuckDb;

    private string BuildConnectionString(ConnectionProfile profile)
    {
        // profile.Database holds a file path, or empty for an in memory database.
        var dataSource = profile.Database.Length == 0 ? ":memory:" : profile.Database;
        return $"Data Source={dataSource}";
    }

    public async Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        _connection = new DuckDBConnection(BuildConnectionString(profile));
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
            await using var conn = new DuckDBConnection(BuildConnectionString(profile));
            await conn.OpenAsync(ct);
            return new TestResult(true, "Connection succeeded.");
        }
        catch (Exception ex)
        {
            return new TestResult(false, ex.Message);
        }
    }

    public async Task<QueryResult> ExecuteReadAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, int maxRows = 1000, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        AdapterHelper.ApplyParameters(cmd, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await AdapterHelper.ReadAsync(reader, maxRows, ct);
    }

    public async Task<int> ExecuteWriteAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        AdapterHelper.ApplyParameters(cmd, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SchemaModel> IntrospectSchemaAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);

        var tables = new List<TableModel>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('system') ORDER BY table_schema, table_name;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add(new TableModel(reader.GetString(1), reader.GetString(0), new List<ColumnModel>(), new List<string>()));
            }
        }

        foreach (var table in tables)
        {
            await FillColumnsAsync(table, ct);
        }

        return new SchemaModel { Engine = "duckdb", Tables = tables };
    }

    private async Task FillColumnsAsync(TableModel table, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DESCRIBE " + Quote(table.Schema, table.Name) + ";";
        var columns = new List<ColumnModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // DESCRIBE returns column_name, column_type, null, key, default, extra
            columns.Add(new ColumnModel(reader.GetString(0), reader.GetString(1), true));
        }

        table.Columns.AddRange(columns);
    }

    private static string Quote(string schema, string table) =>
        schema == "main" ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

    private async Task EnsureOpen(CancellationToken ct)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("OpenAsync must be called before queries.");
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct);
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
