namespace QueryLantern.Tests;

using System.Collections.Generic;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies the confidence scorer down-ranks ungrounded, flagged, or corrected answers and that
/// low-confidence answers trigger an "I am not sure" disclaimer.
/// </summary>
public class ConfidenceTests
{
    [Fact]
    public void Clean_Grounded_Validated_Answer_Is_High_Confidence()
    {
        var service = new ConfidenceService();
        var grounding = new GroundingResult(new List<ClaimCheck> { new("42", true) }, false, false);
        var critique = new CritiqueResult(CritiqueVerdict.Approved, new List<string>());
        var score = service.Compute(grounding, critique, planValidated: true, correctionAttempts: 0);
        Assert.Equal(ConfidenceLevel.High, score.Level);
        Assert.False(score.ShouldDisclaim);
    }

    [Fact]
    public void Empty_Data_Drops_Below_Threshold_And_Disclaims()
    {
        var service = new ConfidenceService();
        var grounding = new GroundingResult(new List<ClaimCheck>(), true, false);
        var critique = new CritiqueResult(CritiqueVerdict.NeedsRevision, new List<string> { "flag" });
        var score = service.Compute(grounding, critique, planValidated: false, correctionAttempts: 1);
        Assert.True(score.ShouldDisclaim);
        Assert.Equal(ConfidenceLevel.Low, score.Level);
    }

    [Fact]
    public void Untraced_Figures_Reduce_Confidence()
    {
        var service = new ConfidenceService();
        var grounding = new GroundingResult(new List<ClaimCheck> { new("999", false) }, false, false);
        var critique = new CritiqueResult(CritiqueVerdict.Approved, new List<string>());
        var score = service.Compute(grounding, critique, planValidated: true, correctionAttempts: 0);
        Assert.True(score.Score < 1.0);
    }
}
