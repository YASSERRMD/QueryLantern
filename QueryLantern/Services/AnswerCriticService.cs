namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A verifier pass that reviews the analyst's answer against the data and plan before it settles.
/// The critic is deterministic (rule-based) so it can run without a second model call, but it
/// consumes the grounding result produced by AnswerGroundingService to catch hallucinated or
/// unsupported claims. When the critic finds blocking issues the answer is marked NeedsRevision
/// and the UI asks the user to confirm or revise.
/// </summary>
public sealed class AnswerCriticService
{
    public CritiqueResult Critique(string question, string answer, GroundingResult grounding)
    {
        var issues = new List<string>();

        if (grounding.HasEmptyOrNullResult && MentionsFindings(answer))
        {
            issues.Add("The answer asserts findings even though the query returned no data.");
        }

        if (grounding.AggregateMismatch)
        {
            issues.Add("A reported total disagrees with the number of rows actually returned.");
        }

        var untraced = grounding.Claims.Where(c => !c.Traced).Select(c => c.Figure).ToList();
        if (untraced.Count > 0)
        {
            issues.Add($"Figures not found in the data: {string.Join(", ", untraced)}.");
        }

        if (!MentionsQuestionTopic(question, answer))
        {
            issues.Add("The answer does not appear to address the question that was asked.");
        }

        if (issues.Count == 0)
        {
            return new CritiqueResult(CritiqueVerdict.Approved, issues);
        }

        return new CritiqueResult(CritiqueVerdict.NeedsRevision, issues)
        {
            Suggestion = "Review the flagged issues. Re-run the analysis or ask a clarifying question before trusting this answer."
        };
    }

    private static bool MentionsFindings(string answer)
    {
        var lowered = answer.ToLowerInvariant();
        return lowered.Contains("found") || lowered.Contains("result") || lowered.Contains("total")
            || lowered.Contains("there are") || lowered.Contains("we have") || lowered.Contains("shows");
    }

    private static bool MentionsQuestionTopic(string question, string answer)
    {
        var words = question
            .ToLowerInvariant()
            .Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 3 && !CommonStopwords.Contains(w))
            .ToList();

        if (words.Count == 0)
        {
            return true;
        }

        var answerLowered = answer.ToLowerInvariant();
        return words.Any(w => answerLowered.Contains(w));
    }

    private static readonly HashSet<string> CommonStopwords = new()
    {
        "what", "which", "show", "list", "give", "tell", "how", "many", "much", "does", "from",
        "with", "that", "this", "have", "were", "been", "will", "would", "could", "should"
    };
}
