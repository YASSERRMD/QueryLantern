namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Exercises the full local stack (adapter + tools) end to end without any network or model, proving
/// the governed query and schema pipelines work against a real database.
/// </summary>
public class EndToEndPipelineTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_e2e_{Guid.NewGuid():N}.db");
    private readonly SqliteAdapter _adapter;

    public EndToEndPipelineTests()
    {
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE sales (region TEXT, amount INTEGER);").GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("INSERT INTO sales VALUES ('EU', 10), ('US', 25), ('EU', 15);").GetAwaiter().GetResult();
    }

    [Fact]
    public void Read_Query_Through_Tools_Returns_Rows()
    {
        var query = new QueryTools(_adapter);
        var schema = new SchemaTools(_adapter, new SchemaCache());
        var list = schema.ListTables();
        Assert.Contains("sales", list);

        var result = query.RunQuery("SELECT region, SUM(amount) AS total FROM sales GROUP BY region ORDER BY region;");
        Assert.Contains("\"rowCount\":2", result);
        Assert.Contains("EU", result);
        Assert.Contains("US", result);
    }

    [Fact]
    public void Write_Is_Staged_Not_Executed()
    {
        var write = new WriteTools(_adapter);
        var staged = write.ProposeWrite("DELETE FROM sales WHERE region = 'US';");
        Assert.Contains("staged", staged);

        var after = _adapter.ExecuteReadAsync("SELECT COUNT(*) AS c FROM sales;").GetAwaiter().GetResult();
        Assert.Equal(3L, after.Rows[0][0]);
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
