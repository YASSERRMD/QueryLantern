namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Scores the confidence of a finalized analyst answer from the signals collected during the run:
/// grounding quality, critique verdict, plan validation, and how many self-correction attempts were
/// spent. When the score falls below the threshold the answer is flagged for an "I am not sure"
/// disclaimer so the model does not assert an unsupported conclusion.
/// </summary>
public sealed class ConfidenceService
{
    private const double Threshold = 0.5;

    public ConfidenceScore Compute(
        GroundingResult grounding,
        CritiqueResult critique,
        bool planValidated,
        int correctionAttempts)
    {
        var reasons = new List<string>();
        var penalties = 0.0;

        if (grounding.HasEmptyOrNullResult)
        {
            penalties += 0.5;
            reasons.Add("No data was returned by the query.");
        }

        if (grounding.AggregateMismatch)
        {
            penalties += 0.25;
            reasons.Add("A reported total disagreed with the row count.");
        }

        var untraced = grounding.Claims.Count(c => !c.Traced);
        if (untraced > 0)
        {
            penalties += System.Math.Min(0.4, 0.1 * untraced);
            reasons.Add($"{untraced} figure(s) could not be traced to the data.");
        }

        if (!critique.Approved)
        {
            penalties += 0.3;
            reasons.Add("The verifier flagged the answer.");
        }

        if (planValidated)
        {
            reasons.Add("The execution plan was validated.");
        }
        else
        {
            penalties += 0.1;
            reasons.Add("The execution plan was not validated.");
        }

        if (correctionAttempts > 0)
        {
            penalties += System.Math.Min(0.3, 0.1 * correctionAttempts);
            reasons.Add($"{correctionAttempts} self-correction attempt(s) were needed.");
        }

        var score = System.Math.Max(0.0, System.Math.Min(1.0, 1.0 - penalties));
        var level = score >= 0.75 ? ConfidenceLevel.High : score >= Threshold ? ConfidenceLevel.Medium : ConfidenceLevel.Low;

        return new ConfidenceScore(score, level, reasons);
    }
}
