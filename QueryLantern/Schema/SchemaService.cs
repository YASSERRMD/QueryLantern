namespace QueryLantern.Schema;

using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Models;

/// <summary>
/// High level schema access for the UI and the agent. Combines the per connection cache with the
/// compact serializer so callers get either a typed model or a prompt ready description.
/// </summary>
public sealed class SchemaService
{
    private readonly SchemaCache _cache;

    public SchemaService(SchemaCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Returns the cached schema, introspecting on demand when absent.
    /// </summary>
    public Task<Adapters.SchemaModel> GetSchemaAsync(
        int connectionId,
        ConnectionProfile profile,
        string? password,
        CancellationToken ct = default) =>
        _cache.GetOrIntrospectAsync(connectionId, profile, password, ct);

    /// <summary>
    /// Refreshes the cache and returns the new schema.
    /// </summary>
    public Task<Adapters.SchemaModel> RefreshAsync(
        int connectionId,
        ConnectionProfile profile,
        string? password,
        CancellationToken ct = default) =>
        _cache.RefreshAsync(connectionId, profile, password, ct);

    /// <summary>
    /// Produces the compact text description used to prime the agent with schema context.
    /// </summary>
    public string ToAgentPrompt(int connectionId) =>
        _cache.Get(connectionId) is { } schema ? SchemaSerializer.ToCompactText(schema) : string.Empty;
}
