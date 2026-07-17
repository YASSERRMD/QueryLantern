namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using QueryLantern.Models;

/// <summary>
/// MySQL or MariaDB adapter backed by MySqlConnector. Implements the full
/// <see cref="IDatabaseAdapter"/> contract.
/// </summary>
public sealed class MySqlAdapter : IDatabaseAdapter, IDisposable
{
    private MySqlConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.MySql;

    private string BuildConnectionString(ConnectionProfile profile, string? password)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = profile.Host,
            Port = (uint)(profile.Port == 0 ? 3306 : profile.Port),
            Database = profile.Database,
            UserID = profile.Username,
            AllowUserVariables = true
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
        _connection = new MySqlConnection(BuildConnectionString(profile, password));
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
            await using var conn = new MySqlConnection(BuildConnectionString(profile, password));
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
            cmd.CommandText = """
                SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME;
                """;
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

        return new SchemaModel { Engine = "mysql", Tables = tables };
    }

    private async Task FillColumnsAsync(TableModel table, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        cmd.Parameters.Add(new MySqlParameter("@schema", table.Schema));
        cmd.Parameters.Add(new MySqlParameter("@table", table.Name));
        var columns = new List<ColumnModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ColumnModel(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES"));
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
