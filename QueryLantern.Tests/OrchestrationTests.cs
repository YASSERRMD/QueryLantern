namespace QueryLantern.Tests;

using System.Collections.Generic;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies multi-query orchestration: deduplication, caps, and passing filter values (ids) from one
/// query's result set into a later query.
/// </summary>
public class OrchestrationTests
{
    private static QueryResult MakeResult(params object[] ids)
    {
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var id in ids)
        {
            rows.Add(new List<object?> { id });
        }
        return new QueryResult
        {
            Columns = new List<ColumnMeta> { new("id", "INTEGER") },
            Rows = rows,
            TruncatedAt = ids.Length
        };
    }

    [Fact]
    public void Chain_Passes_Ids_From_Query_One_Into_Query_Two()
    {
        var orch = new OrchestrationService();
        var prior = new Dictionary<string, QueryResult>
        {
            ["step_1"] = MakeResult(10, 20, 30)
        };
        var requested = new List<AnalysisQuery>
        {
            new("step_1", "SELECT id FROM customers", new List<string>(), new List<object?>()),
            new("step_2", "SELECT * FROM orders WHERE customer_id IN (...)", new List<string> { "step_1" }, new List<object?>())
        };

        var plan = orch.BuildPlan(requested, prior);

        Assert.Equal(2, plan.Queries.Count);
        var filter = plan.Queries[1].FilterValues;
        Assert.Equal(new object?[] { 10, 20, 30 }, filter);
    }

    [Fact]
    public void Identical_SubQueries_Are_Deduplicated()
    {
        var orch = new OrchestrationService();
        var requested = new List<AnalysisQuery>
        {
            new("step_1", "SELECT 1", new List<string>(), new List<object?>()),
            new("step_2", "SELECT 1", new List<string>(), new List<object?>())
        };

        var plan = orch.BuildPlan(requested, new Dictionary<string, QueryResult>());
        Assert.Single(plan.Queries);
        Assert.Contains(plan.DroppedReasons, r => r.Contains("duplicate"));
    }

    [Fact]
    public void Query_Cap_Is_Enforced()
    {
        var orch = new OrchestrationService(maxQueries: 2);
        var requested = new List<AnalysisQuery>
        {
            new("s1", "SELECT 1", new List<string>(), new List<object?>()),
            new("s2", "SELECT 2", new List<string>(), new List<object?>()),
            new("s3", "SELECT 3", new List<string>(), new List<object?>())
        };

        var plan = orch.BuildPlan(requested, new Dictionary<string, QueryResult>());
        Assert.Equal(2, plan.Queries.Count);
        Assert.Contains(plan.DroppedReasons, r => r.Contains("max queries"));
    }

    [Fact]
    public void Total_Row_Cap_Is_Enforced()
    {
        var orch = new OrchestrationService(maxRowsTotal: 25);
        var results = new List<QueryResult> { MakeResult(1, 2, 3), MakeResult(4, 5, 6) };
        var (total, capped) = orch.TallyRows(results);
        Assert.Equal(6, total);
        Assert.False(capped);

        var big = new List<QueryResult>();
        for (var i = 0; i < 10; i++) big.Add(MakeResult(1, 2, 3, 4, 5));
        var (total2, capped2) = orch.TallyRows(big);
        Assert.True(capped2);
        Assert.Equal(25, total2);
    }
}
