namespace QueryLantern.Models;

/// <summary>
/// A recurring analysis that runs on a schedule and reports what changed since the last run.
/// </summary>
public sealed record ScheduledAnalysis(
    int Id,
    int ConnectionId,
    string Question,
    string Sql,
    string Cadence,
    DateTime? LastRunAt,
    string? LastSummary,
    string? LastResultJson = null);

/// <summary>
/// A computed difference between two consecutive runs of a scheduled analysis.
/// </summary>
public sealed record ChangeSummary(
    int PreviousRowCount,
    int CurrentRowCount,
    int RowDelta,
    IReadOnlyList<string> NotableChanges,
    string? MetricDelta);
