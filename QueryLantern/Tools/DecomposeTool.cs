namespace QueryLantern.Tools;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using QueryLantern.Models;

/// <summary>
/// Splits a compound analyst question into an ordered set of answerable sub-questions. Decomposition is
/// local (no model call) and driven by linguistic signals: comparisons, conjunctions, and time framing.
/// Each sub-question can be fed to the planner to produce its own sub-plan.
/// </summary>
public sealed class DecomposeTool
{
    /// <summary>
    /// Produces a Decomposition for the given question, returned as serialized JSON so it can be used
    /// directly as an Ancora tool result.
    /// </summary>
    public string Decompose(string question)
    {
        var decomposition = Build(question);
        return JsonSerializer.Serialize(decomposition);
    }

    /// <summary>
    /// Builds the Decomposition object. A comparison ("compare A vs B", "A versus B") yields two
    /// sub-questions (one per side) plus a synthesis step. A conjunction ("X and Y") yields one
    /// sub-question per clause. A time-framed question adds a trend sub-question. A simple question
    /// yields a single sub-question.
    /// </summary>
    public Decomposition Build(string question)
    {
        var q = (question ?? string.Empty).Trim();
        var lower = q.ToLowerInvariant();

        var subQuestions = new List<SubQuestion>();
        var edges = new List<PlanEdge>();
        var rationale = new List<string>();

        // Comparison pattern: "compare A vs B" / "A versus B".
        var comparison = SplitComparison(q, lower);
        if (comparison.HasValue)
        {
            var (left, right) = comparison.Value;
            subQuestions.Add(new SubQuestion("sq_1", left, new List<string>()));
            subQuestions.Add(new SubQuestion("sq_2", right, new List<string>()));
            subQuestions.Add(new SubQuestion("sq_3", $"Synthesize a comparison of '{left}' versus '{right}'", new List<string> { "sq_1", "sq_2" }));
            edges.Add(new PlanEdge("sq_1", "sq_3"));
            edges.Add(new PlanEdge("sq_2", "sq_3"));
            rationale.Add("Detected a comparison; split into the two sides plus a synthesis step.");
        }
        else if (lower.Contains(" and ") || lower.Contains(", ") || lower.Contains("; "))
        {
            var clauses = q.Split(new[] { " and ", ", ", "; " }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToArray();
            for (var i = 0; i < clauses.Length; i++)
            {
                subQuestions.Add(new SubQuestion($"sq_{i + 1}", clauses[i], new List<string>()));
            }
            subQuestions.Add(new SubQuestion($"sq_{clauses.Length + 1}", "Synthesize the combined answer from the above sub-questions", subQuestions.Select(s => s.Id).ToList()));
            foreach (var sq in subQuestions.Where(s => s.Id != $"sq_{clauses.Length + 1}"))
            {
                edges.Add(new PlanEdge(sq.Id, $"sq_{clauses.Length + 1}"));
            }
            rationale.Add("Detected multiple clauses; split into one sub-question per clause plus a synthesis step.");
        }
        else if (lower.Contains("over time") || lower.Contains("trend") || lower.Contains("by month") || lower.Contains("by year") || lower.Contains("by week"))
        {
            subQuestions.Add(new SubQuestion("sq_1", q, new List<string>()));
            subQuestions.Add(new SubQuestion("sq_2", $"Summarize the trend across the time periods in: {q}", new List<string> { "sq_1" }));
            edges.Add(new PlanEdge("sq_1", "sq_2"));
            rationale.Add("Detected a time framing; added a trend-summary sub-question.");
        }
        else
        {
            subQuestions.Add(new SubQuestion("sq_1", q, new List<string>()));
            rationale.Add("Question is atomic; a single sub-question covers it.");
        }

        return new Decomposition(q, subQuestions, edges, string.Join(" ", rationale));
    }

    /// <summary>
    /// Composes independent sub-answers into a single final answer. Sub-answers that are synthesis steps
    /// are dropped (they are instructions, not content); the rest are joined in order.
    /// </summary>
    public string Compose(IReadOnlyList<string> subAnswers)
    {
        var kept = subAnswers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Where(a => !a.StartsWith("Synthesize", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        return string.Join("\n\n", kept);
    }

    private static (string Left, string Right)? SplitComparison(string question, string lower)
    {
        string? marker = null;
        foreach (var m in new[] { " versus ", " vs ", " vs. ", " compare " })
        {
            if (lower.Contains(m))
            {
                marker = m;
                break;
            }
        }

        if (marker is null) return null;

        if (lower.Contains("compare "))
        {
            // "compare A and B" -> sides after "compare " split on " and ".
            var after = question[(lower.IndexOf("compare ") + "compare ".Length)..].Trim();
            var parts = after.Split(new[] { " and ", " to ", " with " }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }
            return (after, after);
        }

        var idx = lower.IndexOf(marker);
        var left = question[..idx].Trim();
        var right = question[(idx + marker.Length)..].Trim();
        if (left.Length == 0 || right.Length == 0) return null;
        return (left, right);
    }
}
