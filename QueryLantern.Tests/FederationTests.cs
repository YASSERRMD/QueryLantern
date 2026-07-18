namespace QueryLantern.Tests;

using System.IO;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies cross-connection federation joins two databases on a shared key using the in-process
/// equi-join fallback.
/// </summary>
public class FederationTests : System.IDisposable
{
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), $"ql_fed_{System.Guid.NewGuid():N}.db");
    private readonly string _left = Path.Combine(Path.GetTempPath(), $"ql_fedL_{System.Guid.NewGuid():N}.db");
    private readonly string _right = Path.Combine(Path.GetTempPath(), $"ql_fedR_{System.Guid.NewGuid():N}.db");

    public FederationTests()
    {
        CreateTable(_left, "users", "id INTEGER, name TEXT", ("1", "'alice'"), ("2", "'bob'"));
        CreateTable(_right, "orders", "uid INTEGER, total INTEGER", ("1", "100"), ("2", "200"));
    }

    [Fact]
    public async Task Join_Across_Connections_Matches_On_Key()
    {
        var repo = new ConnectionRepository(new CatalogStore(_catalog));
        var lId = repo.InsertAsync(new ConnectionProfile { Name = "L", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _left, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();
        var rId = repo.InsertAsync(new ConnectionProfile { Name = "R", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _right, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();

        var svc = new FederationService(repo, new SecretVault(_catalog + ".vault"));
        var result = await svc.JoinAsync(new FederationRequest(
            new FederationSide(lId, "users", "id"),
            new FederationSide(rId, "orders", "uid")));

        Assert.Equal(2, result.RowCount);
        Assert.Contains(result.Columns, c => c.Name == "L.name");
        Assert.Contains(result.Columns, c => c.Name == "R.total");
    }

    [Fact]
    public async Task Join_With_No_Match_Returns_Empty()
    {
        var repo = new ConnectionRepository(new CatalogStore(_catalog));
        var lId = repo.InsertAsync(new ConnectionProfile { Name = "L", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _left, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();
        var rId = repo.InsertAsync(new ConnectionProfile { Name = "R", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _right, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();

        var svc = new FederationService(repo, new SecretVault(_catalog + ".vault"));
        var result = await svc.JoinAsync(new FederationRequest(
            new FederationSide(lId, "users", "id"),
            new FederationSide(rId, "orders", "uidXX")));
        Assert.Equal(0, result.RowCount);
    }

    private static void CreateTable(string file, string table, string cols, params (string, string)[] rows)
    {
        var adapter = new SqliteAdapter();
        adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = file }, null).GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync($"CREATE TABLE {table} ({cols});").GetAwaiter().GetResult();
        foreach (var (a, b) in rows)
        {
            adapter.ExecuteWriteAsync($"INSERT INTO {table} VALUES ({a}, {b});").GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        foreach (var f in new[] { _catalog, _left, _right, _catalog + ".vault" })
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
