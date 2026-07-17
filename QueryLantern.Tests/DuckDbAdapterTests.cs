namespace QueryLantern.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using Xunit;

public class DuckDbAdapterTests
{
    private static bool NativeAvailable()
    {
        try
        {
            // Trigger the static initializer that loads the native duckdb library.
            _ = new DuckDB.NET.Data.DuckDBConnectionStringBuilder();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Query_Local_Csv_Returns_Rows()
    {
        if (!NativeAvailable())
        {
            return;
        }

        var csv = Path.Combine(Path.GetTempPath(), $"ql_duck_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csv, "id,name,score\n1,Ada,9.5\n2,Linus,8.1\n");
        try
        {
            using var adapter = new DuckDbAdapter();
            var profile = new ConnectionProfile { Engine = DatabaseEngine.DuckDb, Database = ":memory:" };
            await adapter.OpenAsync(profile, null);
            var sql = $"SELECT id, name, score FROM read_csv_auto('{csv.Replace("'", "''")}') ORDER BY id;";
            var result = await adapter.ExecuteReadAsync(sql);
            Assert.Equal(3, result.Columns.Count);
            Assert.Equal(2, result.RowCount);
            Assert.Equal("Ada", result.Rows[0][1]);
        }
        finally
        {
            if (File.Exists(csv))
            {
                File.Delete(csv);
            }
        }
    }

    [Fact]
    public async Task Introspect_Inline_Table_Returns_Columns()
    {
        if (!NativeAvailable())
        {
            return;
        }

        using var adapter = new DuckDbAdapter();
        var profile = new ConnectionProfile { Engine = DatabaseEngine.DuckDb, Database = ":memory:" };
        await adapter.OpenAsync(profile, null);
        await adapter.ExecuteWriteAsync("CREATE TABLE metrics (ts TIMESTAMP, value DOUBLE);");
        var schema = await adapter.IntrospectSchemaAsync();
        Assert.Contains(schema.Tables, t => t.Name == "metrics");
    }
}
