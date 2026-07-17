namespace QueryLantern.Tests;

using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using Xunit;

public class SchemaCacheTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_schema_{System.Guid.NewGuid():N}.db");
    private SqliteAdapter _adapter = null!;
    private SchemaCache _cache = null!;

    public async Task InitializeAsync()
    {
        _adapter = new SqliteAdapter();
        var profile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db };
        await _adapter.OpenAsync(profile, null);
        await _adapter.ExecuteWriteAsync("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT NOT NULL, total REAL);");
        await _adapter.ExecuteWriteAsync("INSERT INTO orders (customer, total) VALUES ('A', 10.5);");
        _adapter.Dispose();
        _cache = new SchemaCache();
    }

    public async Task DisposeAsync()
    {
        if (File.Exists(_db))
        {
            File.Delete(_db);
        }
    }

    [Fact]
    public async Task Introspection_Produces_Stable_SchemaModel()
    {
        var profile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db };
        var first = await _cache.RefreshAsync(1, profile, null);
        var second = await _cache.GetOrIntrospectAsync(1, profile, null);

        Assert.Same(first, second);
        var table = System.Linq.Enumerable.First(first.Tables, t => t.Name == "orders");
        Assert.Contains(table.Columns, c => c.Name == "customer" && !c.IsNullable);
        Assert.Contains(table.Columns, c => c.Name == "total");
    }

    [Fact]
    public async Task CompactText_Is_Deterministic()
    {
        var profile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db };
        var schema = await _cache.RefreshAsync(2, profile, null);
        var text = SchemaSerializer.ToCompactText(schema);
        Assert.Contains("orders", text);
        Assert.Contains("customer", text);
        // Re-serializing the same schema yields identical text.
        Assert.Equal(text, SchemaSerializer.ToCompactText(schema));
    }
}
