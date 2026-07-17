namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QueryLantern.Models;

/// <summary>
/// Oracle Database adapter (12c and later) backed by Oracle.ManagedDataAccess. Implements the full
/// <see cref="IDatabaseAdapter"/> contract. The Data Source uses host:port/service name form.
/// </summary>
public sealed class OracleAdapter : IDatabaseAdapter, IDisposable
{
    private OracleConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    private string BuildConnectionString(ConnectionProfile profile, string? password)
    {
        // profile.Database holds the service name or SID. The Options field may supply a TNS alias.
        var dataSource = profile.Options.Length > 0
            ? profile.Options
            : $"{profile.Host}:{(profile.Port == 0 ? 1521 : profile.Port)}/{profile.Database}";
        var builder = new OracleConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = profile.Username,
            Password = password ?? string.Empty
        };
        return builder.ConnectionString;
    }

    public async Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        _connection = new OracleConnection(BuildConnectionString(profile, password));
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
            await using var conn = new OracleConnection(BuildConnectionString(profile, password));
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
                SELECT OWNER, TABLE_NAME FROM ALL_TABLES
                WHERE OWNER = USER ORDER BY TABLE_NAME
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

        return new SchemaModel { Engine = "oracle", Tables = tables };
    }

    private async Task FillColumnsAsync(TableModel table, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, NULLABLE
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = :schema AND TABLE_NAME = :table
            ORDER BY COLUMN_ID
            """;
        cmd.Parameters.Add(new OracleParameter("schema", table.Schema));
        cmd.Parameters.Add(new OracleParameter("table", table.Name));
        var columns = new List<ColumnModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ColumnModel(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "Y"));
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
