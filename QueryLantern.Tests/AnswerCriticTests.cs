namespace QueryLantern.Tests;

using System.Collections.Generic;
using QueryLantern.Adapters;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies the answer critique pass blocks answers that are ungrounded, contradict the data,
/// or fail to address the question before they settle.
/// </summary>
public class AnswerCriticTests
{
    private static QueryResult MakeResult(params object?[] values)
    {
        var rows = new List<IReadOnlyList<object?>> { values };
        return new QueryResult
        {
            Columns = new List<ColumnMeta> { new("total", "INTEGER") },
            Rows = rows,
            TruncatedAt = 1
        };
    }

    private static GroundingResult Ground(QueryResult result, bool empty = false, bool mismatch = false)
    {
        var results = empty ? new List<QueryResult>() : new List<QueryResult> { result };
        var claims = new List<ClaimCheck>();
        if (mismatch)
        {
            claims.Add(new ClaimCheck("5", true));
        }

        return new GroundingResult(claims, empty, mismatch);
    }

    [Fact]
    public void Asserting_Findings_On_Empty_Data_Is_Rejected()
    {
        var critic = new AnswerCriticService();
        var grounding = Ground(MakeResult(42), empty: true);
        var result = critic.Critique("How many orders?", "We found 42 orders in the data.", grounding);
        Assert.Equal(CritiqueVerdict.NeedsRevision, result.Verdict);
    }

    [Fact]
    public void Untraced_Figure_Is_Rejected()
    {
        var critic = new AnswerCriticService();
        var grounding = new GroundingResult(
            new List<ClaimCheck> { new("999", false) }, false, false);
        var result = critic.Critique("What is the total?", "The total is 999.", grounding);
        Assert.Equal(CritiqueVerdict.NeedsRevision, result.Verdict);
    }

    [Fact]
    public void Clean_Grounded_Answer_Is_Approved()
    {
        var critic = new AnswerCriticService();
        var grounding = new GroundingResult(
            new List<ClaimCheck> { new("42", true) }, false, false);
        var result = critic.Critique("How many orders?", "There are 42 orders.", grounding);
        Assert.Equal(CritiqueVerdict.Approved, result.Verdict);
    }

    [Fact]
    public void Answer_Ignoring_Question_Is_Rejected()
    {
        var critic = new AnswerCriticService();
        var grounding = new GroundingResult(new List<ClaimCheck>(), false, false);
        var result = critic.Critique("How many cancelled orders last month?", "The weather is nice today.", grounding);
        Assert.Equal(CritiqueVerdict.NeedsRevision, result.Verdict);
    }
}
