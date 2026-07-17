namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Tools;

/// <summary>
/// Builds, validates, and persists an analyst plan for a user question, and exposes the current plan to
/// the UI so it can be rendered and tracked. The plan is produced by the planner tool, validated for
/// cycles and tool references, then stored against the connection for auditability.
/// </summary>
public sealed class PlanService
{
    private readonly PlannerTool _planner;
    private readonly PlanRepository _plans;
    private readonly SchemaCache _schemaCache;

    public PlanGraph? Current { get; private set; }
    public PlanValidationResult? LastValidation { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    public PlanService(PlannerTool planner, PlanRepository plans, SchemaCache schemaCache)
    {
        _planner = planner;
        _plans = plans;
        _schemaCache = schemaCache;
    }

    /// <summary>
    /// Plans for a question on a given connection: builds the graph, validates it, and (on success)
    /// persists it. The plan is exposed through <see cref="Current"/> and the Changed event.
    /// </summary>
    public async Task<PlanValidationResult> PlanAsync(string question, int connectionId)
    {
        LastError = null;
        try
        {
            var summary = _schemaCache.Get(connectionId) is { } model
                ? SchemaSerializer.ToCompactText(model)
                : null;
            var graph = _planner.Build(question, summary);
            var validator = new PlanValidator(AgentToolbox.KnownToolNames);
            var result = validator.Validate(graph);
            LastValidation = result;
            Current = result.IsValid ? graph : null;

            if (result.IsValid)
            {
                var payload = JsonSerializer.Serialize(graph);
                await _plans.InsertAsync(new StoredPlan(0, connectionId, question, payload, DateTime.UtcNow));
            }

            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Current = null;
            LastValidation = new PlanValidationResult(false, new List<string> { ex.Message });
            Changed?.Invoke();
            return LastValidation;
        }
    }

    /// <summary>
    /// Loads a previously persisted plan by id and exposes it as the current plan.
    /// </summary>
    public async Task<PlanGraph?> LoadAsync(int id)
    {
        var stored = await _plans.GetAsync(id);
        if (stored is null)
        {
            return null;
        }

        var graph = JsonSerializer.Deserialize<PlanGraph>(stored.Payload);
        Current = graph;
        Changed?.Invoke();
        return graph;
    }

    public void Clear()
    {
        Current = null;
        LastValidation = null;
        LastError = null;
        Changed?.Invoke();
    }
}
