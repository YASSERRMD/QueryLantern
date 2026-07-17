namespace QueryLantern.Tests;

using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using Xunit;

public class SqliteAdapterTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_sqlite_{System.Guid.NewGuid():N}.db");
    private SqliteAdapter _adapter = null!;

    public async Task InitializeAsync()
    {
        _adapter = new SqliteAdapter();
        var profile = new ConnectionProfile { Name = "tmp", Engine = DatabaseEngine.Sqlite, Database = _db };
        await _adapter.OpenAsync(profile, null);
        await _adapter.ExecuteWriteAsync("CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);");
        await _adapter.ExecuteWriteAsync("INSERT INTO people (name, age) VALUES ('Ada', 36);");
        await _adapter.ExecuteWriteAsync("INSERT INTO people (name, age) VALUES ('Linus', 54);");
    }

    public async Task DisposeAsync()
    {
        await _adapter.CloseAsync();
        _adapter.Dispose();
        if (File.Exists(_db))
        {
            File.Delete(_db);
        }
    }

    [Fact]
    public async Task TestConnection_Succeeds()
    {
        var result = await _adapter.TestConnectionAsync(new ConnectionProfile { Database = _db }, null);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteRead_Returns_Rows_And_Columns()
    {
        var result = await _adapter.ExecuteReadAsync("SELECT id, name, age FROM people ORDER BY id;");
        Assert.Equal(3, result.Columns.Count);
        Assert.Equal(2, result.RowCount);
        Assert.Equal("Ada", result.Rows[0][1]);
        Assert.Equal(36, Convert.ToInt32(result.Rows[0][2]));
    }

    [Fact]
    public async Task ExecuteRead_Honors_RowCap()
    {
        var result = await _adapter.ExecuteReadAsync("SELECT * FROM people;", maxRows: 1);
        Assert.Equal(1, result.RowCount);
        Assert.NotNull(result.TruncatedAt);
    }

    [Fact]
    public async Task IntrospectSchema_Returns_Table()
    {
        var schema = await _adapter.IntrospectSchemaAsync();
        Assert.Equal("sqlite", schema.Engine);
        Assert.Contains(schema.Tables, t => t.Name == "people");
        var table = System.Linq.Enumerable.First(schema.Tables, t => t.Name == "people");
        Assert.Contains(table.Columns, c => c.Name == "name");
    }

    [Fact]
    public async Task Parameterized_Query_Works()
    {
        var result = await _adapter.ExecuteReadAsync(
            "SELECT name FROM people WHERE age = @age;",
            new System.Collections.Generic.Dictionary<string, object?> { ["age"] = 54 });
        Assert.Single(result.Rows);
        Assert.Equal("Linus", result.Rows[0][0]);
    }
}
