namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using QueryLantern.Models;

/// <summary>
/// ClickHouse adapter over the ClickHouse.Client HTTP protocol. Implements the full
/// <see cref="IDatabaseAdapter"/> contract. Reads use the columnar HTTP endpoint.
/// </summary>
public sealed class ClickHouseAdapter : IDatabaseAdapter, IDisposable
{
    private ClickHouseConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.ClickHouse;

    private string BuildConnectionString(ConnectionProfile profile, string? password)
    {
        var builder = new ClickHouseConnectionStringBuilder
        {
            Host = profile.Host,
            Port = (ushort)(profile.Port == 0 ? 8443 : profile.Port),
            Database = profile.Database.Length == 0 ? "default" : profile.Database,
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
        _connection = new ClickHouseConnection(BuildConnectionString(profile, password));
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
            await using var conn = new ClickHouseConnection(BuildConnectionString(profile, password));
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
        cmd.CommandText = AppendLimit(sql, maxRows);
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
        await cmd.ExecuteNonQueryAsync(ct);
        return 0;
    }

    public async Task<SchemaModel> IntrospectSchemaAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureOpen(ct);

        var tables = new List<TableModel>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT database, name FROM system.tables WHERE database NOT IN ('system') ORDER BY database, name;";
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

        return new SchemaModel { Engine = "clickhouse", Tables = tables };
    }

    private async Task FillColumnsAsync(TableModel table, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DESCRIBE TABLE " + Quote(table.Schema, table.Name) + ";";
        var columns = new List<ColumnModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // DESCRIBE returns name, type, default_type, default_expression, ...
            columns.Add(new ColumnModel(reader.GetString(0), reader.GetString(1), true));
        }

        table.Columns.AddRange(columns);
    }

    private static string Quote(string schema, string table) => $"`{schema}`.`{table}`";

    private static string AppendLimit(string sql, int maxRows)
    {
        // ClickHouse uses LIMIT. Only append when the query does not already have one.
        if (maxRows <= 0 || sql.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return sql;
        }

        return sql.TrimEnd(';', ' ', '\n', '\r') + $" LIMIT {maxRows}";
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
