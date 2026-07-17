namespace QueryLantern.Tests;

using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Services;
using QueryLantern.Schema;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Verifies the plan graph maps onto an Ancora GraphSpec and that dependency ordering runs the fetch
/// step before the aggregate step. A live model run is skipped because it requires a provider endpoint.
/// </summary>
public class GraphRunTests
{
    private static PlanGraph TwoStepPlan()
    {
        var fetch = new PlanStep { Id = "step_1", Intent = "Fetch the rows", Tool = "run_query", DependsOn = new List<string>() };
        var aggregate = new PlanStep { Id = "step_2", Intent = "Aggregate the result", Tool = "answer", DependsOn = new List<string> { "step_1" } };
        return new PlanGraph
        {
            Question = "total revenue",
            Steps = new List<PlanStep> { fetch, aggregate },
            Edges = new List<PlanEdge> { new("step_1", "step_2") }
        };
    }

    [Fact]
    public void Plan_Maps_To_GraphSpec_With_One_Node_Per_Step()
    {
        var plan = TwoStepPlan();
        var toolbox = new AgentToolbox(new SchemaCache());
        var service = new GraphRunService(toolbox);
        var spec = service.ToGraphSpec(plan, new Ancora.ProviderConfig("http://localhost/v1", "K", "/v1/chat/completions"), "model", 1000);

        Assert.Equal(2, spec.Nodes.Count);
        Assert.All(spec.Nodes, n => Assert.Equal(Ancora.NodeKind.Agent, n.Kind));
        Assert.Equal("step_1", spec.Nodes[0].Id);
        Assert.Equal("step_2", spec.Nodes[1].Id);
        Assert.Single(spec.Edges);
        Assert.Equal("step_1", spec.Edges[0].From);
        Assert.Equal("step_2", spec.Edges[0].To);
    }

    [Fact]
    public void Topological_Order_Runs_Fetch_Before_Aggregate()
    {
        var plan = TwoStepPlan();
        var order = PlanOrder.TopologicalOrder(plan);
        Assert.Equal(new[] { "step_1", "step_2" }, order);
    }

    [Fact(Skip = "Requires a live OpenAI-compatible provider endpoint")]
    public void Two_Step_Plan_Runs_In_Order_And_Passes_Data_Forward()
    {
        var plan = TwoStepPlan();
        var toolbox = new AgentToolbox(new SchemaCache());
        var service = new GraphRunService(toolbox);
        var provider = new Ancora.ProviderConfig("http://localhost:11434/v1", "ANCORA_API_KEY", "/v1/chat/completions");
        var result = service.RunAsync(plan, provider, "model", new SqliteAdapter(), 1000).GetAwaiter().GetResult();
        Assert.True(result.Succeeded);
    }
}
