namespace QueryLantern.Tests;

using System.Collections.Generic;
using QueryLantern.Adapters;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies result sanity checks and hallucination guards: figures not present in the data are flagged,
/// empty results are detected, and aggregate/row-count mismatches are caught.
/// </summary>
public class AnswerGroundingTests
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

    [Fact]
    public void Fabricated_Total_Not_In_Results_Is_Flagged()
    {
        var grounding = new AnswerGroundingService();
        var results = new List<QueryResult> { MakeResult(42) };
        // The answer claims 999, which is not in the data.
        var untraced = grounding.UntracedFigures("The total is 999.", results);
        Assert.Contains("999", untraced);
    }

    [Fact]
    public void Figure_Present_In_Results_Is_Traced()
    {
        var grounding = new AnswerGroundingService();
        var results = new List<QueryResult> { MakeResult(42) };
        var untraced = grounding.UntracedFigures("The total is 42.", results);
        Assert.DoesNotContain("42", untraced);
    }

    [Fact]
    public void Empty_Result_Is_Detected()
    {
        var grounding = new AnswerGroundingService();
        var empty = new QueryResult
        {
            Columns = new List<ColumnMeta> { new("x", "INTEGER") },
            Rows = new List<IReadOnlyList<object?>>(),
            TruncatedAt = 0
        };
        var result = grounding.Check("Nothing found.", new List<QueryResult> { empty });
        Assert.True(result.HasEmptyOrNullResult);
    }

    [Fact]
    public void Aggregate_Total_Mismatch_Is_Detected()
    {
        var grounding = new AnswerGroundingService();
        var results = new List<QueryResult> { MakeResult(42) };
        var result = grounding.Check("We found a total of 5 rows in the data.", results);
        Assert.True(result.AggregateMismatch);
    }
}
