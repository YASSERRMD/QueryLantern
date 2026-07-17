namespace QueryLantern.Tests;

using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Tools;
using Xunit;

public class SafetyClassifierTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_safety_{System.Guid.NewGuid():N}.db");
    private SqliteAdapter _adapter = null!;

    public async Task InitializeAsync()
    {
        _adapter = new SqliteAdapter();
        var profile = new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db };
        await _adapter.OpenAsync(profile, null);
        await _adapter.ExecuteWriteAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT);");
        await _adapter.ExecuteWriteAsync("INSERT INTO t (v) VALUES ('x');");
    }

    public Task DisposeAsync()
    {
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("DELETE FROM t WHERE id = 1", StatementKind.Write)]
    [InlineData("UPDATE t SET v = 'z' WHERE id = 1", StatementKind.Write)]
    [InlineData("INSERT INTO t (v) VALUES ('y')", StatementKind.Write)]
    [InlineData("DROP TABLE t", StatementKind.Ddl)]
    [InlineData("CREATE TABLE u (id INTEGER)", StatementKind.Ddl)]
    [InlineData("TRUNCATE TABLE t", StatementKind.Ddl)]
    [InlineData("SELECT * FROM t", StatementKind.Read)]
    [InlineData("WITH cte AS (SELECT 1) SELECT * FROM cte", StatementKind.Read)]
    public void Classify_Maps_Statement_To_Kind(string sql, StatementKind expected)
    {
        var kind = new SqlSafetyClassifier().Classify(sql).Kind;
        Assert.Equal(expected, kind);
    }

    [Fact]
    public async Task ProposeWrite_Does_Not_Execute()
    {
        var tools = new WriteTools(_adapter);
        var result = tools.ProposeWrite("DELETE FROM t WHERE id = 1");
        Assert.Contains("\"staged\":true", result);

        // The table must be unchanged: one row still present.
        var after = await _adapter.ExecuteReadAsync("SELECT COUNT(*) AS c FROM t;");
        Assert.Equal(1L, after.Rows[0][0]);
    }

    [Fact]
    public void MultipleStatements_Are_Rejected()
    {
        var classification = new SqlSafetyClassifier().Classify("SELECT 1; DELETE FROM t;");
        Assert.Equal(StatementKind.Unknown, classification.Kind);
    }
}
