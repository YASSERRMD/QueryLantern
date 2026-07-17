namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Tools;

/// <summary>
/// Streamed lifecycle event for a single step of a graph run, attributed to the step that produced it.
/// The UI consumes these to show each step as it starts, streams tokens, and completes or fails.
/// </summary>
public sealed record GraphStepEvent(
    string StepId,
    PlanStepStatus Status,
    string? Detail = null);

/// <summary>
/// Executes a PlanGraph as an Ancora dependency-ordered graph (GraphSpec). Each plan step becomes a
/// graph node carrying step-specific instructions and the governed tools. Per-step output is captured
/// and written back into the plan so it is auditable. The graph halts cleanly on an unrecoverable
/// step failure and surfaces the reason.
/// </summary>
public sealed class GraphRunService
{
    private readonly AgentToolbox _toolbox;

    public GraphRunService(AgentToolbox toolbox)
    {
        _toolbox = toolbox;
    }

    /// <summary>
    /// Maps a PlanGraph onto an Ancora GraphSpec. Each step becomes a GraphNode of kind Agent whose
    /// instructions encode the step intent and tool, and whose tools are the governed toolbox. Edges
    /// mirror the plan dependencies.
    /// </summary>
    public GraphSpec ToGraphSpec(PlanGraph plan, ProviderConfig provider, string model, int maxRows)
    {
        var nodes = new List<GraphNode>();
        foreach (var step in plan.Steps)
        {
            var instructions = BuildStepInstructions(step);
            var tools = _toolbox.BuildReadToolSpecs();
            var spec = new AgentSpec(model, instructions, tools)
            {
                MaxSteps = 6
            };
            nodes.Add(new GraphNode(step.Id, NodeKind.Agent, spec));
        }

        var edges = plan.Edges
            .Select(e => new GraphEdge(e.From, e.To, string.Empty))
            .ToList();
        return new GraphSpec(nodes, edges);
    }

    /// <summary>
    /// Runs the plan graph and reports per-step progress plus the final plan with captured outputs. The
    /// graph is executed by the Ancora runtime; failures are reported through the returned status. The
    /// caller must dispose the returned handle once done.
    /// </summary>
    public async Task<GraphRunResult> RunAsync(
        PlanGraph plan,
        ProviderConfig provider,
        string model,
        IDatabaseAdapter adapter,
        int maxRows,
        IProgress<GraphStepEvent>? progress = null,
        CancellationToken ct = default)
    {
        var runtime = new Runtime(provider);
        try
        {
            // Register the governed read handlers on the shared runtime once so every graph node can
            // call any read tool during the run.
            _toolbox.RegisterReadHandlers(runtime, adapter, maxRows, sql => progress?.Report(new GraphStepEvent("run_query", PlanStepStatus.Running, sql)));
            var nodes = new List<GraphNode>();
            foreach (var step in plan.Steps)
            {
                var instructions = BuildStepInstructions(step);
                var tools = _toolbox.BuildReadToolSpecs();
                var spec = new AgentSpec(model, instructions, tools) { MaxSteps = 6 };
                nodes.Add(new GraphNode(step.Id, NodeKind.Agent, spec));
            }

            var edges = plan.Edges.Select(e => new GraphEdge(e.From, e.To, string.Empty)).ToList();
            var graph = new GraphSpec(nodes, edges);
            var agent = new Agent(runtime);
            var handle = agent.RunGraph(graph);

            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var failed = new List<string>();
            var currentNode = string.Empty;
            await foreach (var ev in handle.EventsAsync(ct))
            {
                switch (ev)
                {
                    case StartedEvent se:
                        // The StartedEvent Spec carries the graph node id for graph runs.
                        currentNode = se.Spec;
                        progress?.Report(new GraphStepEvent(currentNode, PlanStepStatus.Running, currentNode));
                        break;
                    case TokenEvent te:
                        if (!string.IsNullOrEmpty(currentNode))
                        {
                            progress?.Report(new GraphStepEvent(currentNode, PlanStepStatus.Running, te.Text));
                        }
                        break;
                    case CompletedEvent ce:
                        // Attribute the completed output to the node that most recently started.
                        var id = currentNode;
                        if (!string.IsNullOrEmpty(id))
                        {
                            outputs[id] = ce.Output;
                            progress?.Report(new GraphStepEvent(id, PlanStepStatus.Completed, ce.Output));
                        }
                        break;
                    case FailedEvent fe:
                        failed.Add(currentNode);
                        progress?.Report(new GraphStepEvent(currentNode, PlanStepStatus.Failed, fe.Error));
                        break;
                }
            }

            var updatedSteps = plan.Steps.Select(s => outputs.TryGetValue(s.Id, out var o)
                ? s with { Status = PlanStepStatus.Completed, Output = o }
                : s with { Status = failed.Contains(s.Id) ? PlanStepStatus.Failed : s.Status }).ToList();

            return new GraphRunResult(
                plan with { Steps = updatedSteps },
                failed.Count == 0,
                failed.Count == 0 ? null : string.Join("; ", failed.Select(f => $"Step {f} failed")));
        }
        catch (Exception ex)
        {
            return new GraphRunResult(plan, false, ex.Message);
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static string BuildStepInstructions(PlanStep step)
    {
        var baseInstruction = step.Tool switch
        {
            "list_tables" => "Use the list_tables tool to explore the schema, then return a concise summary of what you found.",
            "describe_table" => "Use the describe_table tool to inspect the relevant table, then summarize its columns.",
            "sample_rows" => "Use the sample_rows tool to preview data, then summarize what you observed.",
            "run_query" => "Use the run_query tool with the SQL needed for this step, then summarize the returned rows.",
            "explain_plan" => "Use the explain_plan tool to inspect the execution plan and summarize it.",
            "answer" => "Synthesize the final answer from the results produced by earlier steps. Be concise and ground every figure in the data.",
            _ => $"Perform the step using the {step.Tool} tool and summarize the outcome."
        };
        return $"You are executing one step of an analyst plan. Intent: {step.Intent}. {baseInstruction}";
    }
}

/// <summary>
/// The outcome of running a plan graph: the plan with captured step outputs and a status flag plus an
/// optional failure reason.
/// </summary>
public sealed record GraphRunResult(PlanGraph Plan, bool Succeeded, string? FailureReason);
