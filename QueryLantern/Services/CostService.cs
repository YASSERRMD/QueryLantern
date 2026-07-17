namespace QueryLantern.Services;

using System.Text.Json;

/// <summary>
/// Accumulates per-run cost records so the user can see how much each conversation and model costs.
/// Records are persisted as append-only JSON lines and aggregated on read.
/// </summary>
public sealed class CostService
{
    private readonly object _gate = new();
    private readonly string _path;

    public CostService(string path)
    {
        _path = path;
    }

    public sealed record CostRecord(string RunId, DateTime Timestamp, string Provider, string Model, decimal TotalUsd);

    public void Record(string runId, string provider, string model, decimal totalUsd)
    {
        lock (_gate)
        {
            var record = new CostRecord(runId, DateTime.UtcNow, provider, model, totalUsd);
            File.AppendAllText(_path, JsonSerializer.Serialize(record) + "\n");
        }
    }

    public IReadOnlyList<CostRecord> ReadAll()
    {
        if (!File.Exists(_path)) return new List<CostRecord>();
        var result = new List<CostRecord>();
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(JsonSerializer.Deserialize<CostRecord>(line) ?? throw new InvalidOperationException("Corrupt cost record."));
        }
        return result;
    }

    public decimal TotalUsd() => ReadAll().Sum(r => r.TotalUsd);
}
