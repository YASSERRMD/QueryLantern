namespace QueryLantern.Services;

using System.Collections.Generic;

/// <summary>
/// Outcome of a grounding check over a natural-language answer.
/// </summary>
public sealed record GroundingResult(
    IReadOnlyList<ClaimCheck> Claims,
    bool HasEmptyOrNullResult,
    bool AggregateMismatch)
{
    public string? Note { get; init; }
}

/// <summary>
/// A single numeric figure extracted from the answer and whether it was traced to the data.
/// </summary>
public sealed record ClaimCheck(string Figure, bool Traced);
