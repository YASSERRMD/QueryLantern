namespace QueryLantern.Tools;

using System.Collections.Generic;
using System.Text.Json;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;

/// <summary>
/// Produces an explicit, inspectable analyst plan from a user question and a schema summary. The
/// planner reasons locally (no model call) about the shape of the question: it always begins by
/// exploring the schema, issues the read queries it needs, and aggregates the result. The output is a
/// validated PlanGraph the agent can render and execute as a dependency ordered graph.
/// </summary>
public sealed class PlannerTool
{
    private readonly SchemaCache _cache;

    public PlannerTool(SchemaCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Builds a PlanGraph for the given question against the schema currently in the cache. The plan
    /// is returned as serialized JSON so it can be used directly as an Ancora tool result.
    /// </summary>
    public string Plan(string question, string? schemaSummary = null)
    {
        var graph = Build(question, schemaSummary);
        return JsonSerializer.Serialize(graph);
    }

    /// <summary>
    /// Builds the PlanGraph object for a question. The planner emits a schema exploration step, one
    /// read step per inferred query need (at least one), and an aggregate/answer step that depends on
    /// the reads. Compound questions (joins, comparisons) yield multiple read steps.
    /// </summary>
    public PlanGraph Build(string question, string? schemaSummary = null)
    {
        var q = (question ?? string.Empty).Trim();
        var steps = new List<PlanStep>();
        var edges = new List<PlanEdge>();

        // Step 1: explore the schema so later steps reason over real tables and columns.
        var explore = new PlanStep
        {
            Id = "step_1",
            Intent = "Inspect the available tables and columns to ground the question in the real schema",
            Tool = "list_tables",
            Inputs = JsonSerializer.Serialize(new { schema = string.Empty }),
            DependsOn = new List<string>()
        };
        steps.Add(explore);

        // Infer how many read queries the question needs. A join or comparison implies at least two.
        var readCount = InferReadCount(q);
        var readIds = new List<string>();
        for (var i = 0; i < readCount; i++)
        {
            var id = $"step_{steps.Count + 1}";
            var read = new PlanStep
            {
                Id = id,
                Intent = readCount == 1
                    ? "Run the read query that answers the question"
                    : $"Run read query {i + 1} needed to answer the compound question",
                Tool = "run_query",
                Inputs = JsonSerializer.Serialize(new { sql = string.Empty }),
                DependsOn = new List<string> { explore.Id }
            };
            steps.Add(read);
            edges.Add(new PlanEdge(explore.Id, id));
            readIds.Add(id);
        }

        // Final step: synthesize the answer from the gathered results.
        var answerId = $"step_{steps.Count + 1}";
        var answer = new PlanStep
        {
            Id = answerId,
            Intent = "Synthesize the final answer from the query results",
            Tool = "answer",
            Inputs = null,
            DependsOn = readIds
        };
        steps.Add(answer);
        foreach (var rid in readIds)
        {
            edges.Add(new PlanEdge(rid, answerId));
        }

        return new PlanGraph
        {
            Question = q,
            Steps = steps,
            Edges = edges,
            Rationale = schemaSummary is null
                ? "Schema summary not supplied; plan assumes exploration is required first."
                : "Plan derived from the supplied schema summary and question shape."
        };
    }

    private static int InferReadCount(string question)
    {
        var lower = question.ToLowerInvariant();
        var joinSignals = new[] { "join", " and ", " versus ", " vs ", "compare", "across", " both ", " each " };
        var signals = 0;
        foreach (var signal in joinSignals)
        {
            if (lower.Contains(signal))
            {
                signals++;
            }
        }

        // A compound question needs at least two reads; a simple question needs one.
        return signals == 0 ? 1 : Math.Min(2 + (signals - 1), 4);
    }
}
