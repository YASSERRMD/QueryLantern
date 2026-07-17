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
        GraphRunOptions options = default,
        CancellationToken ct = default)
    {
        var runtime = new Runtime(provider);
        try
        {
            // Outputs captured from prior steps, passed forward as typed input to dependent steps.
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var failed = new List<string>();
            // Register the governed read handlers on the shared runtime once so every graph node can
            // call any read tool during the run.
            _toolbox.RegisterReadHandlers(runtime, adapter, maxRows, sql => progress?.Report(new GraphStepEvent("run_query", PlanStepStatus.Running, sql)));
            var nodes = new List<GraphNode>();
            foreach (var step in plan.Steps)
            {
                // Pass each dependency's captured output forward as typed input to this step.
                var resolvedInput = StepInputResolver.Resolve(step, outputs);
                var instructions = BuildStepInstructions(step) + resolvedInput;
                var tools = _toolbox.BuildReadToolSpecs();
                var spec = new AgentSpec(model, instructions, tools) { MaxSteps = 6 };
                nodes.Add(new GraphNode(step.Id, NodeKind.Agent, spec));
            }

            var edges = plan.Edges.Select(e => new GraphEdge(e.From, e.To, string.Empty)).ToList();
            var graph = new GraphSpec(nodes, edges);
            var agent = new Agent(runtime);
            var handle = agent.RunGraph(graph);

            var currentNode = string.Empty;
            await foreach (var ev in handle.EventsAsync(ct))
            {
                // Attribute every event to the step that produced it so the UI can render per-step
                // status distinctly (Started, Token, Completed, Failed) as the graph runs.
                currentNode = AttributeEvent(ev, currentNode, outputs, failed, progress, options, out var halt);
                if (halt)
                {
                    return new GraphRunResult(
                        plan with { Steps = plan.Steps.Select(s => s with { Status = failed.Contains(s.Id) ? PlanStepStatus.Failed : s.Status }).ToList() },
                        false,
                        $"Step {currentNode} failed: unrecoverable.");
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

    /// <summary>
    /// Routes a single graph event to the right per-step status update, capturing outputs and recording
    /// failures. Returns the node id that is currently active and a flag indicating the run must halt
    /// (fail-fast on an unrecoverable step failure).
    /// </summary>
    private static string AttributeEvent(
        RunEvent ev,
        string activeNode,
        Dictionary<string, string> outputs,
        List<string> failed,
        IProgress<GraphStepEvent>? progress,
        GraphRunOptions options,
        out bool halt)
    {
        halt = false;
        switch (ev)
        {
            case StartedEvent se:
                progress?.Report(new GraphStepEvent(se.Spec, PlanStepStatus.Running, se.Spec));
                return se.Spec;
            case TokenEvent te:
                if (!string.IsNullOrEmpty(activeNode))
                {
                    progress?.Report(new GraphStepEvent(activeNode, PlanStepStatus.Running, te.Text));
                }
                return activeNode;
            case CompletedEvent ce:
                if (!string.IsNullOrEmpty(activeNode))
                {
                    outputs[activeNode] = ce.Output;
                    progress?.Report(new GraphStepEvent(activeNode, PlanStepStatus.Completed, ce.Output));
                }
                return activeNode;
            case FailedEvent fe:
                failed.Add(activeNode);
                progress?.Report(new GraphStepEvent(activeNode, PlanStepStatus.Failed, fe.Error));
                if (options.FailFast)
                {
                    halt = true;
                }
                return activeNode;
            default:
                return activeNode;
        }
    }
}

/// <summary>
/// The outcome of running a plan graph: the plan with captured step outputs and a status flag plus an
/// optional failure reason.
/// </summary>
public sealed record GraphRunResult(PlanGraph Plan, bool Succeeded, string? FailureReason);

/// <summary>
/// Options that tune graph execution. <see cref="FailFast"/> stops the run at the first unrecoverable
/// step failure instead of continuing independent branches.
/// </summary>
public readonly record struct GraphRunOptions
{
    public bool FailFast { get; init; }
}

/// <summary>
/// Resolves a step's typed inputs by inlining the captured outputs of its dependencies. This is how data
/// passes forward between steps: a later step receives the earlier step's result as context in its
/// instructions so the agent can act on real data rather than repeating a query.
/// </summary>
public static class StepInputResolver
{
    public static string Resolve(PlanStep step, IReadOnlyDictionary<string, string> outputs)
    {
        if (step.DependsOn.Count == 0)
        {
            return string.Empty;
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (var dep in step.DependsOn)
        {
            if (outputs.TryGetValue(dep, out var value))
            {
                parts.Add($"\nPrior step '{dep}' produced: {value}");
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join("\n", parts);
    }
}
