namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;

/// <summary>
/// Manages the semantic layer: business terms mapped to schema expressions. Terms are used to translate
/// natural-language questions ("revenue", "by region") into the correct columns/expressions and are
/// surfaced to the model as a synonym/metric/dimension glossary.
/// </summary>
public sealed class SemanticLayerService
{
    private readonly SemanticLayerRepository _repository;

    public SemanticLayerService(SemanticLayerRepository repository)
    {
        _repository = repository;
    }

    public Task<int> AddTermAsync(int connectionId, string term, SemanticTermKind kind, string expression, string? table = null)
    {
        var t = (term ?? string.Empty).Trim();
        var e = (expression ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(e))
        {
            return Task.FromResult(0);
        }

        return _repository.AddAsync(connectionId, t, kind, e, string.IsNullOrWhiteSpace(table) ? null : table.Trim());
    }

    public Task<IReadOnlyList<SemanticTerm>> ListAsync(int connectionId) => _repository.ListAsync(connectionId);

    public Task DeleteAsync(int id) => _repository.DeleteAsync(id);

    /// <summary>
    /// Builds a glossary block from a connection's semantic terms, or empty string when none.
    /// </summary>
    public async Task<string> BuildGlossaryAsync(int connectionId)
    {
        var terms = await _repository.ListAsync(connectionId);
        if (terms.Count == 0)
        {
            return string.Empty;
        }

        var lines = terms.Select(t => t.Table is null
            ? $"- \"{t.Term}\" ({t.Kind}) => {t.Expression}"
            : $"- \"{t.Term}\" ({t.Kind}) => {t.Expression} on {t.Table}");
        return "Semantic layer (business terms -> schema):\n" + string.Join("\n", lines);
    }
}
