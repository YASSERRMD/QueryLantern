namespace QueryLantern.Tests;

using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Tools;
using Xunit;

public class RunQueryToolTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_runq_{System.Guid.NewGuid():N}.db");
    private SqliteAdapter _adapter = null!;

    public async Task InitializeAsync()
    {
        _adapter = new SqliteAdapter();
        var profile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db };
        await _adapter.OpenAsync(profile, null);
        await _adapter.ExecuteWriteAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT);");
        await _adapter.ExecuteWriteAsync("INSERT INTO t (v) VALUES ('x'), ('y');");
    }

    public async Task DisposeAsync()
    {
        await _adapter.CloseAsync();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }

    [Fact]
    public void RunQuery_Returns_Rows_For_Select()
    {
        var tools = new QueryTools(_adapter);
        var json = tools.RunQuery("SELECT id, v FROM t ORDER BY id;");
        Assert.True(json.Contains("\"rowCount\":2"), $"JSON was: {json}");
        Assert.Contains("\"columns\"", json);
        Assert.Contains("\"rows\"", json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"v\"", json);
    }

    [Fact]
    public void RunQuery_Rejects_NonSelect()
    {
        var tools = new QueryTools(_adapter);
        var json = tools.RunQuery("DELETE FROM t;");
        Assert.True(json.Contains("Only read-only statements are allowed"), $"JSON was: {json}");
        Assert.Contains("Write", json);
    }

    [Fact]
    public void Classifier_Marks_Select_As_Read()
    {
        var classifier = new SqlSafetyClassifier();
        Assert.Equal(StatementKind.Read, classifier.Classify("SELECT 1").Kind);
        Assert.Equal(StatementKind.Write, classifier.Classify("UPDATE t SET v='z'").Kind);
        Assert.Equal(StatementKind.Ddl, classifier.Classify("DROP TABLE t").Kind);
    }
}
