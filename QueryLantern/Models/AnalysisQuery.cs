namespace QueryLantern.Models;

using System.Collections.Generic;

/// <summary>
/// A single read query within a multi-query analysis, with the ids drawn from a prior step's result set
/// (for example, the row ids produced by an earlier step) that this query should filter on.
/// </summary>
public sealed record AnalysisQuery(
    string StepId,
    string Sql,
    IReadOnlyList<string> SourceStepIds,
    IReadOnlyList<object?> FilterValues);

/// <summary>
/// The resolved set of queries to run for an analysis after deduplication and cap enforcement, plus the
/// reason any query was dropped. Each query carries the values it should filter on, taken from a prior
/// step's result set.
/// </summary>
public sealed record QueryPlan(
    IReadOnlyList<AnalysisQuery> Queries,
    int TotalRowCap,
    IReadOnlyList<string> DroppedReasons);
