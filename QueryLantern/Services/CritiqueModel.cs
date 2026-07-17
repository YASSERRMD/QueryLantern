namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Verdict of a critique pass over an analyst answer.
/// </summary>
public enum CritiqueVerdict
{
    Approved,
    NeedsRevision
}

/// <summary>
/// Result of reviewing the analyst answer before it settles.
/// </summary>
public sealed record CritiqueResult(
    CritiqueVerdict Verdict,
    IReadOnlyList<string> Issues)
{
    public string? Suggestion { get; init; }
    public bool Approved => Verdict == CritiqueVerdict.Approved;
}
