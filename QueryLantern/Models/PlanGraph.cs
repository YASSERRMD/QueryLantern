namespace QueryLantern.Models;

using System.Collections.Generic;

/// <summary>
/// The lifecycle state of a single plan step as it is executed by the agent graph.
/// </summary>
public enum PlanStepStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// A single, auditable unit of work in an analyst plan. Each step names the tool it intends to call,
/// the intent behind it, the inputs it needs, and any steps it depends on. Steps form a directed graph
/// that the planner produces before any query runs.
/// </summary>
public sealed record PlanStep
{
    /// <summary>The stable identifier of this step within the plan (for example "step_1").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>A short human readable description of what this step is trying to achieve.</summary>
    public string Intent { get; init; } = string.Empty;

    /// <summary>The registered tool this step will invoke (for example "run_query", "list_tables").</summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>Free form inputs the tool needs, serialized as JSON (for example the SQL text).</summary>
    public string? Inputs { get; init; }

    /// <summary>Identifiers of steps that must complete before this one can run.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = new List<string>();

    /// <summary>The current execution status of this step. Starts as Pending.</summary>
    public PlanStepStatus Status { get; init; } = PlanStepStatus.Pending;

    /// <summary>Optional captured output of the step once it completes (for example a result summary).</summary>
    public string? Output { get; init; }
}

/// <summary>
/// An explicit, inspectable plan the agent builds before answering a question. The graph is a list of
/// steps and the dependency edges between them. It is validated (acyclic, tools exist) before execution
/// and persisted against the conversation so every analyst action is auditable.
/// </summary>
public sealed record PlanGraph
{
    /// <summary>The user question this plan was produced to answer.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>The ordered set of steps that make up the plan.</summary>
    public IReadOnlyList<PlanStep> Steps { get; init; } = new List<PlanStep>();

    /// <summary>The dependency edges, expressed as (from step id, to step id) pairs.</summary>
    public IReadOnlyList<PlanEdge> Edges { get; init; } = new List<PlanEdge>();

    /// <summary>When the plan was created (UTC).</summary>
    public System.DateTime CreatedAt { get; init; } = System.DateTime.UtcNow;

    /// <summary>Optional note describing why the planner structured the plan this way.</summary>
    public string? Rationale { get; init; }
}

/// <summary>
/// A directed dependency edge in a plan graph: <paramref name="From"/> must complete before
/// <paramref name="To"/> may run.
/// </summary>
public sealed record PlanEdge(string From, string To);
