namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Maintains a rolling memory of recent conversation turns and resolves elliptical follow-up questions
/// ("and for the EU region?", "what about last month?") against the most recent prior turn so the model
/// receives a fully specified question instead of an ambiguous pronoun.
/// </summary>
public sealed class ConversationMemoryService
{
    private readonly List<ConversationTurn> _turns = new();
    private const int MaxTurns = 10;

    private static readonly string[] FollowUpMarkers =
    {
        "and for", "what about", "how about", "the same", "for the same", "also", "instead",
        "compared to", "in the same", "last month", "this month", "previous", "above", "that", "it", "those", "these"
    };

    public void Record(string question, string sql, string resultSummary)
    {
        _turns.Add(new ConversationTurn(question, sql, resultSummary));
        if (_turns.Count > MaxTurns)
        {
            _turns.RemoveAt(0);
        }
    }

    public IReadOnlyList<ConversationTurn> History => _turns;

    public void Clear() => _turns.Clear();

    public ResolvedQuestion Resolve(string question)
    {
        var trimmed = (question ?? string.Empty).Trim();
        if (_turns.Count == 0)
        {
            return new ResolvedQuestion(trimmed, trimmed, false, null);
        }

        var lowered = trimmed.ToLowerInvariant();
        var isFollowUp = FollowUpMarkers.Any(m => lowered.Contains(m)) || StartsWithContinuation(lowered);
        if (!isFollowUp)
        {
            return new ResolvedQuestion(trimmed, trimmed, false, null);
        }

        var anchor = _turns[^1];
        var resolved = trimmed;
        // Substitute bare pronouns so the model has the prior subject in scope.
        resolved = resolved
            .Replace(" it ", $" {anchor.Question} ")
            .Replace(" that ", $" {anchor.Question} ")
            .Replace(" those ", $" {anchor.Question} ")
            .Replace(" these ", $" {anchor.Question} ");
        resolved = $"{resolved} (context from previous question: \"{anchor.Question}\")";

        return new ResolvedQuestion(trimmed, resolved, true, _turns.Count - 1);
    }

    private static bool StartsWithContinuation(string lowered)
    {
        return lowered.StartsWith("and ") || lowered.StartsWith("but ") || lowered.StartsWith("or ");
    }
}
