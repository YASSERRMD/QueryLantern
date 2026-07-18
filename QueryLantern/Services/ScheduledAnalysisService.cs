namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;

/// <summary>
/// Runs scheduled/recurring analyses and reports what changed since the previous run. The current run is
/// executed read-only against the connection; its result is compared to the previous run's stored result
/// to produce a change summary (row delta, notable changes, metric delta) without requiring a model.
/// </summary>
public sealed class ScheduledAnalysisService
{
    private readonly ScheduleRepository _schedules;
    private readonly ConnectionRepository _connections;
    private readonly SecretVault _vault;

    public ScheduledAnalysisService(ScheduleRepository schedules, ConnectionRepository connections, SecretVault vault)
    {
        _schedules = schedules;
        _connections = connections;
        _vault = vault;
    }

    public Task<IReadOnlyList<ScheduledAnalysis>> ListAsync() => _schedules.ListAsync();

    public Task<int> CreateAsync(int connectionId, string question, string sql, string cadence)
        => _schedules.AddAsync(connectionId, question, sql, cadence);

    public Task DeleteAsync(int id) => _schedules.DeleteAsync(id);

    public async Task<(ScheduledAnalysis Schedule, ChangeSummary Change, QueryResult Result)> RunAsync(int id, CancellationToken ct = default)
    {
        var schedule = await _schedules.GetAsync(id)
            ?? throw new InvalidOperationException($"Schedule {id} not found.");
        var result = await ExecuteAsync(schedule, ct);
        var previous = string.IsNullOrEmpty(schedule.LastSummary) ? null : Deserialize(schedule.LastResultJson);
        var change = Summarize(previous, result);

        var summary = string.IsNullOrEmpty(change.MetricDelta)
            ? $"{change.CurrentRowCount} rows ({(change.RowDelta >= 0 ? "+" : "")}{change.RowDelta} vs previous)"
            : $"{change.CurrentRowCount} rows; {change.MetricDelta}";

        var resultJson = JsonSerializer.Serialize(new
        {
            columns = result.Columns.Select(c => new { c.Name, c.DataType }),
            rows = result.Rows,
            rowCount = result.RowCount,
            truncatedAt = result.TruncatedAt
        });

        await _schedules.UpdateRunWithResultAsync(id, DateTime.UtcNow, summary, resultJson);
        return (schedule, change, result);
    }

    private async Task<QueryResult> ExecuteAsync(ScheduledAnalysis schedule, CancellationToken ct)
    {
        var profile = await _connections.GetAsync(schedule.ConnectionId)
            ?? throw new InvalidOperationException($"Connection {schedule.ConnectionId} not found.");
        var password = string.IsNullOrEmpty(profile.SecretRef) ? null : SafeDecrypt(profile.SecretRef);
        using var adapter = AdapterFactory.Create(profile.Engine);
        await adapter.OpenAsync(profile, password, ct);
        return await adapter.ExecuteReadAsync(schedule.Sql, null, 1000, ct);
    }

    private static ChangeSummary Summarize(QueryResult? previous, QueryResult current)
    {
        var prevCount = previous?.RowCount ?? 0;
        var curCount = current.RowCount;
        var changes = new List<string>();

        if (previous is null)
        {
            changes.Add("First run; no previous baseline to compare.");
        }
        else if (curCount > prevCount)
        {
            changes.Add($"{curCount - prevCount} new row(s) appeared.");
        }
        else if (curCount < prevCount)
        {
            changes.Add($"{prevCount - curCount} row(s) disappeared.");
        }
        else
        {
            changes.Add("Row count unchanged.");
        }

        var metricDelta = ComputeMetricDelta(previous, current);
        return new ChangeSummary(prevCount, curCount, curCount - prevCount, changes, metricDelta);
    }

    private static string? ComputeMetricDelta(QueryResult? previous, QueryResult current)
    {
        if (previous is null || current.Columns.Count != 1 || previous.Columns.Count != 1)
        {
            return null;
        }

        var cur = SingleNumeric(current.Rows);
        var prev = SingleNumeric(previous.Rows);
        if (cur is null || prev is null)
        {
            return null;
        }

        var delta = cur.Value - prev.Value;
        return $"metric {prev.Value} -> {cur.Value} ({(delta >= 0 ? "+" : "")}{delta})";
    }

    private static double? SingleNumeric(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        if (rows.Count != 1 || rows[0].Count != 1)
        {
            return null;
        }

        var v = rows[0][0];
        return v is null ? null : double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static QueryResult? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var columns = new List<ColumnMeta>();
            foreach (var c in root.GetProperty("columns").EnumerateArray())
            {
                columns.Add(new ColumnMeta(c.GetProperty("name").GetString() ?? "", c.GetProperty("dataType").GetString() ?? ""));
            }

            var rows = new List<IReadOnlyList<object?>>();
            foreach (var r in root.GetProperty("rows").EnumerateArray())
            {
                var row = new List<object?>();
                foreach (var cell in r.EnumerateArray())
                {
                    row.Add(cell.ValueKind == JsonValueKind.Null ? null : cell.ToString());
                }

                rows.Add(row);
            }

            return new QueryResult { Columns = columns, Rows = rows, TruncatedAt = rows.Count };
        }
        catch
        {
            return null;
        }
    }

    private string? SafeDecrypt(string reference)
    {
        try
        {
            return _vault.Decrypt(reference);
        }
        catch
        {
            return null;
        }
    }
}
