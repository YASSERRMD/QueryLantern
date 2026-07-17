namespace QueryLantern.Tests;

using System.IO;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Services;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Verifies plan step statuses transition correctly through the UI driven lifecycle (plan -> await
/// approval -> running) without requiring a live model.
/// </summary>
public class PlanStatusTransitionTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_planui_{Guid.NewGuid():N}.db");
    private readonly SqliteAdapter _adapter;
    private readonly SchemaCache _cache = new();

    public PlanStatusTransitionTests()
    {
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE t (id INTEGER);").GetAwaiter().GetResult();
        _cache.RefreshAsync(3, new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
    }

    [Fact]
    public void Plan_Sets_AwaitingApproval_With_All_Steps_Pending()
    {
        var planner = new PlannerTool(_cache);
        var plans = new PlanRepository(new CatalogStore(":memory:"));
        var svc = new PlanService(planner, plans, _cache, new GraphRunService(new AgentToolbox(_cache)));

        var result = svc.PlanAsync("total revenue", 3).GetAwaiter().GetResult();

        Assert.True(result.IsValid);
        Assert.Equal(PlanService.PlanRunState.AwaitingApproval, svc.State);
        Assert.All(svc.Current!.Steps, s => Assert.Equal(PlanStepStatus.Pending, svc.StepStatus[s.Id]));
    }

    [Fact]
    public void ApprovePlan_Moves_State_To_Running()
    {
        var planner = new PlannerTool(_cache);
        var plans = new PlanRepository(new CatalogStore(":memory:"));
        var svc = new PlanService(planner, plans, _cache, new GraphRunService(new AgentToolbox(_cache)));

        svc.PlanAsync("total revenue", 3).GetAwaiter().GetResult();
        svc.ApprovePlan();

        Assert.Equal(PlanService.PlanRunState.Running, svc.State);
    }

    [Fact]
    public void EditStep_Revalidates_The_Plan()
    {
        var planner = new PlannerTool(_cache);
        var plans = new PlanRepository(new CatalogStore(":memory:"));
        var svc = new PlanService(planner, plans, _cache, new GraphRunService(new AgentToolbox(_cache)));

        svc.PlanAsync("total revenue", 3).GetAwaiter().GetResult();
        var readStep = svc.Current!.Steps.First(s => s.Tool == "run_query");
        var validation = svc.EditStep(readStep.Id, "Run a corrected query", "{\"sql\":\"SELECT 1\"}");

        Assert.True(validation.IsValid);
        var updated = svc.Current!.Steps.First(s => s.Id == readStep.Id);
        Assert.Equal("{\"sql\":\"SELECT 1\"}", updated.Inputs);
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
