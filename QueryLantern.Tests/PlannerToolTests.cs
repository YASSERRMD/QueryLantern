namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Services;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Verifies the planner tool produces a valid multi-step plan for a join-and-aggregate question, and
/// that the plan validator accepts it. Uses a real SQLite database so the schema cache is populated.
/// </summary>
public class PlannerToolTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_plan_{Guid.NewGuid():N}.db");
    private readonly SqliteAdapter _adapter;
    private readonly SchemaCache _cache = new();
    private readonly int _connectionId = 7;

    public PlannerToolTests()
    {
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE orders (id INTEGER, customer_id INTEGER, total INTEGER);").GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE customers (id INTEGER, name TEXT);").GetAwaiter().GetResult();
        _cache.RefreshAsync(_connectionId, new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
    }

    [Fact]
    public void Planner_Produces_Valid_MultiStep_Plan_For_Join_Question()
    {
        var planner = new PlannerTool(_cache);
        var question = "Join orders and customers and show total revenue per customer";
        var plan = planner.Build(question, SchemaSerializer.ToCompactText(_cache.Get(_connectionId)!));

        // A join question must produce an explore step, at least two read steps, and an answer step.
        Assert.True(plan.Steps.Count >= 4, $"expected at least 4 steps, got {plan.Steps.Count}");
        Assert.Contains(plan.Steps, s => s.Tool == "list_tables");
        Assert.Contains(plan.Steps, s => s.Tool == "run_query");
        Assert.Contains(plan.Steps, s => s.Tool == "answer");

        // Dependencies must be resolvable and acyclic.
        var validator = new PlanValidator(AgentToolbox.KnownToolNames);
        var result = validator.Validate(plan);
        Assert.True(result.IsValid, "plan should be valid: " + string.Join("; ", result.Errors));

        // The answer step must depend on every read step.
        var answer = plan.Steps[^1];
        var readIds = plan.Steps.Where(s => s.Tool == "run_query").Select(s => s.Id).ToArray();
        foreach (var rid in readIds)
        {
            Assert.Contains(rid, answer.DependsOn);
        }
    }

    [Fact]
    public void Planner_Simple_Question_Produces_Single_Read()
    {
        var planner = new PlannerTool(_cache);
        var plan = planner.Build("What is the total revenue?", null);
        Assert.Single(plan.Steps.Where(s => s.Tool == "run_query"));
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
