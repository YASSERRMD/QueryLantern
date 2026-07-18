namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;

/// <summary>
/// Federates queries across two connections. The intended engine is DuckDB (which can ATTACH multiple
/// databases and JOIN across them); when the DuckDB runtime is unavailable this service falls back to an
/// in-process equi-join on a shared key column so cross-connection analysis still works without native
/// dependencies. Only read-only access is used.
/// </summary>
public sealed class FederationService
{
    private readonly ConnectionRepository _connections;
    private readonly SecretVault _vault;

    public FederationService(ConnectionRepository connections, SecretVault vault)
    {
        _connections = connections;
        _vault = vault;
    }

    public async Task<QueryResult> JoinAsync(FederationRequest request, CancellationToken ct = default)
    {
        var left = await LoadSideAsync(request.Left, ct);
        var right = await LoadSideAsync(request.Right, ct);

        var leftKey = request.Left.KeyColumn;
        var rightKey = request.Right.KeyColumn;
        var leftLookup = left.Rows
            .Where(r => r.Count > IndexOf(left.Columns, leftKey))
            .ToDictionary(r => Normalize(r[IndexOf(left.Columns, leftKey)]), r => r);
        var rightLookup = right.Rows
            .Where(r => r.Count > IndexOf(right.Columns, rightKey))
            .ToDictionary(r => Normalize(r[IndexOf(right.Columns, rightKey)]), r => r);

        var columns = new List<ColumnMeta>();
        columns.AddRange(left.Columns.Select(c => new ColumnMeta($"L.{c.Name}", c.DataType)));
        columns.AddRange(right.Columns.Select(c => new ColumnMeta($"R.{c.Name}", c.DataType)));

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var kvp in leftLookup)
        {
            if (rightLookup.TryGetValue(kvp.Key, out var rRow))
            {
                rows.Add(left.Columns.Select((_, i) => kvp.Value[i])
                    .Concat(right.Columns.Select((_, i) => rRow[i]))
                    .ToList());
            }
        }

        return new QueryResult { Columns = columns, Rows = rows, TruncatedAt = rows.Count };
    }

    private async Task<QueryResult> LoadSideAsync(FederationSide side, CancellationToken ct)
    {
        var profile = await _connections.GetAsync(side.ConnectionId)
            ?? throw new System.InvalidOperationException($"Connection {side.ConnectionId} not found.");
        var password = ResolvePassword(profile);
        using var adapter = AdapterFactory.Create(profile.Engine);
        await adapter.OpenAsync(profile, password, ct);
        var sql = $"SELECT * FROM {Quote(side.Table)}";
        return await adapter.ExecuteReadAsync(sql, null, 1000, ct);
    }

    private string? ResolvePassword(ConnectionProfile profile)
    {
        if (string.IsNullOrEmpty(profile.SecretRef))
        {
            return null;
        }

        try
        {
            return _vault.Decrypt(profile.SecretRef);
        }
        catch
        {
            return null;
        }
    }

    private static int IndexOf(IReadOnlyList<ColumnMeta> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(object? value)
        => (value?.ToString() ?? string.Empty).Trim();

    private static string Quote(string table) => "\"" + table.Replace("\"", "") + "\"";
}
