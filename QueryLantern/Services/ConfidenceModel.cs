namespace QueryLantern.Services;

using System.Collections.Generic;

/// <summary>
/// Confidence band for an analyst answer.
/// </summary>
public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// A scored confidence assessment for a finalized answer, with the reasons that moved the score.
/// </summary>
public sealed record ConfidenceScore(
    double Score,
    ConfidenceLevel Level,
    IReadOnlyList<string> Reasons)
{
    public bool ShouldDisclaim => Score < 0.5;
}
