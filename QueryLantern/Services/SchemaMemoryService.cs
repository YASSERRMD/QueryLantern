namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;

/// <summary>
/// Manages durable learned facts about a connection's schema. Facts are recorded by the agent when it
/// discovers a quirk, synonym or convention, and are later surfaced to the model as grounding context
/// so future analyses do not rediscover known facts.
/// </summary>
public sealed class SchemaMemoryService
{
    private readonly SchemaMemoryRepository _repository;

    public SchemaMemoryService(SchemaMemoryRepository repository)
    {
        _repository = repository;
    }

    public Task<int> RecordAsync(int connectionId, string fact, SchemaFactKind kind = SchemaFactKind.Quirk)
    {
        var trimmed = (fact ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Task.FromResult(0);
        }

        return _repository.AddAsync(connectionId, trimmed, kind);
    }

    public Task<IReadOnlyList<SchemaFact>> ListAsync(int connectionId) => _repository.ListAsync(connectionId);

    public Task DeleteAsync(int id) => _repository.DeleteAsync(id);

    /// <summary>
    /// Builds a compact context block of known facts for a connection, or an empty string when none.
    /// </summary>
    public async Task<string> SummarizeForPromptAsync(int connectionId)
    {
        var facts = await _repository.ListAsync(connectionId);
        if (facts.Count == 0)
        {
            return string.Empty;
        }

        var lines = facts.Select(f => $"- [{f.Kind}] {f.Fact}");
        return "Known facts about this database (learned previously):\n" + string.Join("\n", lines);
    }
}
