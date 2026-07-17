namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using QueryLantern.Models;

/// <summary>
/// PostgreSQL adapter backed by Npgsql. Implements the full <see cref="IDatabaseAdapter"/> contract.
/// </summary>
public sealed class PostgresAdapter : IDatabaseAdapter, IDisposable
{
    private NpgsqlConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.Postgresql;

    private string BuildConnectionString(ConnectionProfile profile, string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port == 0 ? 5432 : profile.Port,
            Database = profile.Database,
            Username = profile.Username
        };
        if (password is not null)
        {
            builder.Password = password;
        }

        if (profile.Options.Length > 0)
        {
            builder.ConnectionString = builder.ConnectionString + ";" + profile.Options;
        }

        return builder.ConnectionString;
    }

    public async Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        _connection = new NpgsqlConnection(BuildConnectionString(profile, password));
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
            await using var conn = new NpgsqlConnection(BuildConnectionString(profile, password));
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
        const string sql = """
            SELECT n.nspname AS schema, c.relname AS table
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
            ORDER BY n.nspname, c.relname;
            """;
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var schema = reader.GetString(0);
                var tableName = reader.GetString(1);
                tables.Add(new TableModel(tableName, schema, new List<ColumnModel>(), new List<string>()));
            }
        }

        foreach (var table in tables)
        {
            await FillColumnsAsync(table, ct);
        }

        return new SchemaModel { Engine = "postgresql", Tables = tables };
    }

    private async Task FillColumnsAsync(TableModel table, CancellationToken ct)
    {
        const string sql = """
            SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod), a.attnotnull
            FROM pg_attribute a
            WHERE a.attrelid = (($1 || '.' || $2)::regclass) AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum;
            """;
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@p1", table.Schema));
        cmd.Parameters.Add(new NpgsqlParameter("@p2", table.Name));
        var columns = new List<ColumnModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ColumnModel(reader.GetString(0), reader.GetString(1), !reader.GetBoolean(2)));
        }

        table.Columns.AddRange(columns);
    }

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
