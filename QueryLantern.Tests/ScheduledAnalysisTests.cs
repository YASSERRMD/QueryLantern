namespace QueryLantern.Tests;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies scheduled analyses execute and report what changed since the previous run.
/// </summary>
public class ScheduledAnalysisTests : System.IDisposable
{
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), $"ql_sched_{System.Guid.NewGuid():N}.db");
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"ql_schedD_{System.Guid.NewGuid():N}.db");

    public ScheduledAnalysisTests()
    {
        var adapter = new SqliteAdapter();
        adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _data }, null).GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync("CREATE TABLE m (v INTEGER);").GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync("INSERT INTO m VALUES (10);").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task First_Run_Baselines_And_Second_Run_Reports_Change()
    {
        var repo = new ConnectionRepository(new CatalogStore(_catalog));
        var connId = repo.InsertAsync(new ConnectionProfile { Name = "D", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _data, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();
        var schedules = new ScheduleRepository(new CatalogStore(_catalog));
        var svc = new ScheduledAnalysisService(schedules, repo, new SecretVault(_catalog + ".vault"));

        var id = await svc.CreateAsync(connId, "total", "SELECT SUM(v) AS total FROM m", "daily");

        var first = await svc.RunAsync(id);
        Assert.Equal(1, first.Change.CurrentRowCount);
        Assert.Contains("First run", first.Change.NotableChanges[0]);

        // Change the underlying data and run again.
        var adapter = new SqliteAdapter();
        adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _data }, null).GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync("UPDATE m SET v = 25;").GetAwaiter().GetResult();

        var second = await svc.RunAsync(id);
        Assert.Equal("metric 10 -> 25 (+15)", second.Change.MetricDelta);
    }

    [Fact]
    public async Task Row_Count_Delta_Is_Reported()
    {
        var repo = new ConnectionRepository(new CatalogStore(_catalog));
        var connId = repo.InsertAsync(new ConnectionProfile { Name = "D", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _data, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();
        var schedules = new ScheduleRepository(new CatalogStore(_catalog));
        var svc = new ScheduledAnalysisService(schedules, repo, new SecretVault(_catalog + ".vault"));
        var id = await svc.CreateAsync(connId, "count", "SELECT * FROM m", "daily");

        await svc.RunAsync(id);
        var adapter = new SqliteAdapter();
        adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _data }, null).GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync("INSERT INTO m VALUES (99);").GetAwaiter().GetResult();
        var second = await svc.RunAsync(id);
        Assert.Equal(1, second.Change.RowDelta);
    }

    public void Dispose()
    {
        foreach (var f in new[] { _catalog, _data, _catalog + ".vault" })
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
