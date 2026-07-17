namespace QueryLantern.Tests;

using System.Text.Json;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Tools;
using Xunit;

public class SchemaToolsTests : IDisposable
{
    private readonly string _file;
    private readonly SqliteAdapter _adapter;

    public SchemaToolsTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"ql_schema_{Guid.NewGuid():N}.db");
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _file }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);").GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("INSERT INTO people VALUES (1, 'Ada', 36), (2, 'Linus', 54);").GetAwaiter().GetResult();
    }

    [Fact]
    public void ListTables_Returns_People()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache());
        var json = tools.ListTables();
        Assert.Contains("\"people\"", json);
        Assert.Contains("\"Columns\":3", json);
    }

    [Fact]
    public void DescribeTable_Returns_Columns()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache());
        var json = tools.DescribeTable("people");
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"INTEGER\"", json.ToUpperInvariant());
        Assert.Contains("\"TEXT\"", json.ToUpperInvariant());
    }

    [Fact]
    public void SampleRows_Returns_Rows()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache());
        var json = tools.SampleRows("people", 10);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("rows").GetArrayLength();
        Assert.Equal(2, rows);
        Assert.Contains("Ada", json);
    }

    [Fact]
    public void SampleRows_Respects_Cap()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache(), sampleCap: 1);
        var json = tools.SampleRows("people", 100);
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("rows").GetArrayLength();
        Assert.Equal(1, rows);
    }

    [Fact]
    public void ExplainPlan_Returns_Plan_Rows()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache());
        var json = tools.ExplainPlan("SELECT * FROM people");
        Assert.Contains("columns", json);
        Assert.Contains("rows", json);
    }

    [Fact]
    public void DescribeTable_Unknown_Returns_Error()
    {
        var tools = new SchemaTools(_adapter, new SchemaCache());
        var json = tools.DescribeTable("ghost");
        Assert.Contains("\"error\"", json);
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_file)) File.Delete(_file);
    }
}
