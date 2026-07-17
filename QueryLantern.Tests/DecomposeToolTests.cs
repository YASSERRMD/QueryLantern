namespace QueryLantern.Tests;

using QueryLantern.Models;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Verifies the decompose tool splits a compound "compare A vs B" question into the expected
/// sub-questions and composes sub-answers correctly.
/// </summary>
public class DecomposeToolTests
{
    [Fact]
    public void Compare_Question_Decomposes_Into_Two_Sides_Plus_Synthesis()
    {
        var tool = new DecomposeTool();
        var decomposition = tool.Build("Compare revenue in EU versus US over the last year");

        // Two sides plus a synthesis step.
        Assert.Equal(3, decomposition.SubQuestions.Count);
        Assert.Contains(decomposition.SubQuestions, s => s.Text.Contains("EU"));
        Assert.Contains(decomposition.SubQuestions, s => s.Text.Contains("US"));
        var synthesis = decomposition.SubQuestions[^1];
        Assert.Contains("Synthesize", synthesis.Text);
        Assert.Equal(new[] { "sq_1", "sq_2" }, synthesis.DependsOn);
    }

    [Fact]
    public void Conjunction_Question_Splits_Into_Clauses()
    {
        var tool = new DecomposeTool();
        var decomposition = tool.Build("Show total users and total revenue for each region");

        Assert.Equal(3, decomposition.SubQuestions.Count);
        Assert.Contains(decomposition.SubQuestions, s => s.Text.Contains("total users"));
        Assert.Contains(decomposition.SubQuestions, s => s.Text.Contains("total revenue"));
    }

    [Fact]
    public void Atomic_Question_Yields_Single_SubQuestion()
    {
        var tool = new DecomposeTool();
        var decomposition = tool.Build("What is the total revenue?");
        Assert.Single(decomposition.SubQuestions);
    }

    [Fact]
    public void Compose_Drops_Synthesis_Instructions_And_Joins_Answers()
    {
        var tool = new DecomposeTool();
        var composed = tool.Compose(new[] { "EU earned 100.", "US earned 200.", "Synthesize a comparison of EU versus US" });
        Assert.Contains("EU earned 100.", composed);
        Assert.Contains("US earned 200.", composed);
        Assert.DoesNotContain("Synthesize", composed);
    }
}
