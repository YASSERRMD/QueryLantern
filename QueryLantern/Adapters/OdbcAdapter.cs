namespace QueryLantern.Adapters;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Odbc;
using QueryLantern.Models;

/// <summary>
/// Generic ODBC fallback adapter for any database reachable through a system DSN or ODBC connection
/// string. Implements the full <see cref="IDatabaseAdapter"/> contract.
/// </summary>
public sealed class OdbcAdapter : IDatabaseAdapter, IDisposable
{
    private OdbcConnection? _connection;
    private bool _disposed;

    public DatabaseEngine Engine => DatabaseEngine.Odbc;

    private string BuildConnectionString(ConnectionProfile profile)
    {
        // profile.Database holds the DSN name when Options is empty, otherwise Options is the full
        // ODBC connection string and Database is ignored.
        if (profile.Options.Length > 0)
        {
            return profile.Options;
        }

        return $"DSN={profile.Database}";
    }

    public async Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default)
    {
        var builder = new OdbcConnectionStringBuilder(BuildConnectionString(profile));
        if (password is not null && !builder.ContainsKey("pwd") && !builder.ContainsKey("password"))
        {
            builder["pwd"] = password;
        }

        if (profile.Username.Length > 0 && !builder.ContainsKey("uid") && !builder.ContainsKey("user"))
        {
            builder["uid"] = profile.Username;
        }

        _connection = new OdbcConnection(builder.ConnectionString);
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
            var builder = new OdbcConnectionStringBuilder(BuildConnectionString(profile));
            if (password is not null)
            {
                builder["pwd"] = password;
            }

            if (profile.Username.Length > 0)
            {
                builder["uid"] = profile.Username;
            }

            await using var conn = new OdbcConnection(builder.ConnectionString);
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
        var schemaRows = _connection!.GetSchema("Tables");
        foreach (System.Data.DataRow row in schemaRows.Rows)
        {
            var name = row["TABLE_NAME"]?.ToString() ?? string.Empty;
            var schema = row["TABLE_SCHEMA"]?.ToString() ?? "default";
            if (row["TABLE_TYPE"]?.ToString() == "TABLE")
            {
                tables.Add(new TableModel(name, schema, new List<ColumnModel>(), new List<string>()));
            }
        }

        foreach (var table in tables)
        {
            var columnRows = _connection.GetSchema("Columns", new[] { null, null, table.Name, null });
            foreach (System.Data.DataRow row in columnRows.Rows)
            {
                var colName = row["COLUMN_NAME"]?.ToString() ?? string.Empty;
                var colType = row["DATA_TYPE"]?.ToString() ?? "unknown";
                table.Columns.Add(new ColumnModel(colName, colType, true));
            }
        }

        return new SchemaModel { Engine = "odbc", Tables = tables };
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
