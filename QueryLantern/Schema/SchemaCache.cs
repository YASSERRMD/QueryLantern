namespace QueryLantern.Schema;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Models;

/// <summary>
/// Caches introspected schemas per connection so the agent and UI do not re-introspect on every
/// request. Refresh is explicit (manual) to keep the cache predictable and cheap.
/// </summary>
public sealed class SchemaCache
{
    private readonly ConcurrentDictionary<int, CacheEntry> _entries = new();

    private sealed record CacheEntry(SchemaModel Schema, DateTime CachedAt);

    /// <summary>
    /// Returns the cached schema for a connection, or null if it has not been loaded yet.
    /// </summary>
    public SchemaModel? Get(int connectionId) =>
        _entries.TryGetValue(connectionId, out var entry) ? entry.Schema : null;

    /// <summary>
    /// Gets the schema for a connection, introspecting on demand when not cached.
    /// </summary>
    public async Task<SchemaModel> GetOrIntrospectAsync(
        int connectionId,
        ConnectionProfile profile,
        string? password,
        CancellationToken ct = default)
    {
        if (_entries.TryGetValue(connectionId, out var entry))
        {
            return entry.Schema;
        }

        return await RefreshAsync(connectionId, profile, password, ct);
    }

    /// <summary>
    /// Forces re-introspection and replaces the cached schema for the connection.
    /// </summary>
    public async Task<SchemaModel> RefreshAsync(
        int connectionId,
        ConnectionProfile profile,
        string? password,
        CancellationToken ct = default)
    {
        using var adapter = AdapterFactory.Create(profile.Engine);
        await adapter.OpenAsync(profile, password, ct);
        var schema = await adapter.IntrospectSchemaAsync(ct);
        await adapter.CloseAsync(ct);
        _entries[connectionId] = new CacheEntry(schema, DateTime.UtcNow);
        return schema;
    }

    /// <summary>
    /// Drops the cached schema for a connection (for example after a schema changing operation).
    /// </summary>
    public void Invalidate(int connectionId) => _entries.TryRemove(connectionId, out _);
}
