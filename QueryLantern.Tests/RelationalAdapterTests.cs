namespace QueryLantern.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using Xunit;

/// <summary>
/// Integration tests for the PostgreSQL and MySQL adapters. They are skipped unless a DSN is
/// supplied through the environment (QL_TEST_POSTGRES or QL_TEST_MYSQL), so they never block CI.
/// </summary>
public class RelationalAdapterTests
{
    private static ConnectionProfile ProfileFromDsn(string dsn)
    {
        // dsn form: host:port/database/user/password
        var parts = dsn.Split('/');
        var hostPort = parts[0].Split(':');
        return new ConnectionProfile
        {
            Host = hostPort[0],
            Port = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 0,
            Database = parts[1],
            Username = parts[2]
        };
    }

    [Fact(Skip = "Set QL_TEST_POSTGRES to a DSN (host:port/db/user/pass) to run.")]
    public async Task Postgres_Connect_Read_Introspect()
    {
        var dsn = Environment.GetEnvironmentVariable("QL_TEST_POSTGRES")!;
        var secret = Environment.GetEnvironmentVariable("QL_TEST_POSTGRES_PW");
        using var adapter = new PostgresAdapter();
        var profile = ProfileFromDsn(dsn);
        var test = await adapter.TestConnectionAsync(profile, secret);
        Assert.True(test.Success, test.Message);

        await adapter.OpenAsync(profile, secret);
        var schema = await adapter.IntrospectSchemaAsync();
        Assert.NotEmpty(schema.Tables);
        await adapter.CloseAsync();
    }

    [Fact(Skip = "Set QL_TEST_MYSQL to a DSN (host:port/db/user/pass) to run.")]
    public async Task MySql_Connect_Read_Introspect()
    {
        var dsn = Environment.GetEnvironmentVariable("QL_TEST_MYSQL")!;
        var secret = Environment.GetEnvironmentVariable("QL_TEST_MYSQL_PW");
        using var adapter = new MySqlAdapter();
        var profile = ProfileFromDsn(dsn);
        var test = await adapter.TestConnectionAsync(profile, secret);
        Assert.True(test.Success, test.Message);

        await adapter.OpenAsync(profile, secret);
        var schema = await adapter.IntrospectSchemaAsync();
        Assert.NotEmpty(schema.Tables);
        await adapter.CloseAsync();
    }

    [Fact(Skip = "Set QL_TEST_SQLSERVER to a DSN (host:port/db/user/pass) to run.")]
    public async Task SqlServer_Connect_Read_Introspect()
    {
        var dsn = Environment.GetEnvironmentVariable("QL_TEST_SQLSERVER")!;
        var secret = Environment.GetEnvironmentVariable("QL_TEST_SQLSERVER_PW");
        using var adapter = new QueryLantern.Adapters.SqlServerAdapter();
        var profile = ProfileFromDsn(dsn);
        var test = await adapter.TestConnectionAsync(profile, secret);
        Assert.True(test.Success, test.Message);

        await adapter.OpenAsync(profile, secret);
        var schema = await adapter.IntrospectSchemaAsync();
        Assert.NotEmpty(schema.Tables);
        await adapter.CloseAsync();
    }

    [Fact(Skip = "Set QL_TEST_ORACLE to a DSN (host:port/service/user/pass) to run.")]
    public async Task Oracle_Connect_Read_Introspect()
    {
        var dsn = Environment.GetEnvironmentVariable("QL_TEST_ORACLE")!;
        var secret = Environment.GetEnvironmentVariable("QL_TEST_ORACLE_PW");
        using var adapter = new QueryLantern.Adapters.OracleAdapter();
        var profile = ProfileFromDsn(dsn);
        var test = await adapter.TestConnectionAsync(profile, secret);
        Assert.True(test.Success, test.Message);

        await adapter.OpenAsync(profile, secret);
        var schema = await adapter.IntrospectSchemaAsync();
        Assert.NotEmpty(schema.Tables);
        await adapter.CloseAsync();
    }
}
