namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;

/// <summary>
/// A library of successful analyses used for few-shot grounding. New questions are matched against
/// prior questions by shared keywords; the best prior SQL is surfaced to the model as an example so it
/// starts from a known-good pattern instead of generating from scratch.
/// </summary>
public sealed class QueryLibraryService
{
    private readonly QueryLibraryRepository _repository;

    public QueryLibraryService(QueryLibraryRepository repository)
    {
        _repository = repository;
    }

    public Task<int> SaveAsync(int connectionId, string question, string sql, int rating = 0)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(sql))
        {
            return Task.FromResult(0);
        }

        return _repository.AddAsync(connectionId, question.Trim(), sql.Trim(), rating);
    }

    public Task<IReadOnlyList<LibraryQuery>> ListAsync(int connectionId) => _repository.ListAsync(connectionId);

    public Task DeleteAsync(int id) => _repository.DeleteAsync(id);

    /// <summary>
    /// Returns up to <paramref name="limit"/> prior queries whose question shares keywords with the
    /// current one, best matches first.
    /// </summary>
    public async Task<IReadOnlyList<LibraryQuery>> FindSimilarAsync(int connectionId, string question, int limit = 3)
    {
        var all = await _repository.ListAsync(connectionId);
        var tokens = Tokenize(question);
        if (tokens.Count == 0)
        {
            return all.Take(limit).ToList();
        }

        var scored = all
            .Select(q => (Query: q, Score: Score(q.Question, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Query.Rating)
            .Take(limit)
            .Select(x => x.Query)
            .ToList();

        return scored;
    }

    /// <summary>
    /// Builds a few-shot example block from similar prior queries, or empty string when none.
    /// </summary>
    public async Task<string> BuildFewShotAsync(int connectionId, string question)
    {
        var similar = await FindSimilarAsync(connectionId, question);
        if (similar.Count == 0)
        {
            return string.Empty;
        }

        var lines = similar.Select(q => $"Example:\nQuestion: {q.Question}\nSQL: {q.Sql}");
        return "Similar past analyses you can reuse as patterns:\n" + string.Join("\n\n", lines);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return new HashSet<string>(
            text.ToLowerInvariant()
                .Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3 && !CommonStopwords.Contains(w))
                .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())),
            StringComparer.Ordinal);
    }

    private static int Score(string candidate, HashSet<string> tokens)
    {
        var candTokens = Tokenize(candidate);
        return candTokens.Intersect(tokens).Count();
    }

    private static readonly HashSet<string> CommonStopwords = new()
    {
        "what", "which", "show", "list", "give", "tell", "how", "many", "much", "does", "from",
        "with", "that", "this", "have", "were", "been", "will", "would", "could", "should", "the",
        "for", "and", "per", "each", "by"
    };
}
