namespace QueryLantern.Models;

using System.Collections.Generic;

/// <summary>
/// A single sub-question produced by decomposing a compound user question. Each sub-question is planned
/// and answered independently, then composed into the final answer.
/// </summary>
public sealed record SubQuestion(
    string Id,
    string Text,
    IReadOnlyList<string> DependsOn);

/// <summary>
/// The result of decomposing a compound question: an ordered set of sub-questions, the dependency edges
/// between them, and an optional rationale describing how the split was derived.
/// </summary>
public sealed record Decomposition(
    string Question,
    IReadOnlyList<SubQuestion> SubQuestions,
    IReadOnlyList<PlanEdge> Edges,
    string? Rationale = null);
