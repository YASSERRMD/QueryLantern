namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Tools;

/// <summary>
/// Drives the analyst plan lifecycle for the UI: build and validate a plan, let the user review and
/// approve it before execution, run it as an Ancora graph while streaming per-step status, and expose
/// the live plan so the chat surface can render and track it.
/// </summary>
public sealed class PlanService
{
    public enum PlanRunState { Idle, Planning, AwaitingApproval, Running, Done, Failed }

    private readonly PlannerTool _planner;
    private readonly PlanRepository _plans;
    private readonly SchemaCache _schemaCache;
    private readonly GraphRunService _graph;

    public PlanGraph? Current { get; private set; }
    public PlanValidationResult? LastValidation { get; private set; }
    public string? LastError { get; private set; }
    public PlanRunState State { get; private set; } = PlanRunState.Idle;

    /// <summary>Live status of each step, keyed by step id, updated as the graph runs.</summary>
    public IReadOnlyDictionary<string, PlanStepStatus> StepStatus => _stepStatus;

    /// <summary>Captured output per step id, for the expandable detail view.</summary>
    public IReadOnlyDictionary<string, string> StepOutput => _stepOutput;

    /// <summary>Correction attempts recorded per step id, shown as a visible sub-thread in the UI.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CorrectionAttempt>> CorrectionAttempts => _corrections;

    private readonly Dictionary<string, IReadOnlyList<CorrectionAttempt>> _corrections = new(System.StringComparer.Ordinal);

    private readonly Dictionary<string, PlanStepStatus> _stepStatus = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stepOutput = new(System.StringComparer.Ordinal);

    public event Action? Changed;

    public PlanService(PlannerTool planner, PlanRepository plans, SchemaCache schemaCache, GraphRunService graph)
    {
        _planner = planner;
        _plans = plans;
        _schemaCache = schemaCache;
        _graph = graph;
    }

    /// <summary>
    /// Plans for a question on a given connection: builds the graph, validates it, and (on success)
    /// persists it. The plan is exposed through <see cref="Current"/> and the Changed event.
    /// </summary>
    public async Task<PlanValidationResult> PlanAsync(string question, int connectionId)
    {
        LastError = null;
        State = PlanRunState.Planning;
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
            _stepStatus.Clear();
            _stepOutput.Clear();
            if (result.IsValid)
            {
                foreach (var step in graph.Steps)
                {
                    _stepStatus[step.Id] = PlanStepStatus.Pending;
                }
            }

            if (result.IsValid)
            {
                var payload = JsonSerializer.Serialize(graph);
                await _plans.InsertAsync(new StoredPlan(0, connectionId, question, payload, DateTime.UtcNow));
            }

            State = result.IsValid ? PlanRunState.AwaitingApproval : PlanRunState.Failed;
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Current = null;
            State = PlanRunState.Failed;
            LastValidation = new PlanValidationResult(false, new List<string> { ex.Message });
            Changed?.Invoke();
            return LastValidation;
        }
    }

    /// <summary>
    /// Approves the current plan for execution, moving it out of the awaiting-approval gate. The caller
    /// then invokes <see cref="RunAsync"/>.
    /// </summary>
    public void ApprovePlan()
    {
        if (Current is null) return;
        State = PlanRunState.Running;
        Changed?.Invoke();
    }

    /// <summary>
    /// Edits a step in the current plan (for example correcting the SQL intent) before execution. The
    /// plan must be awaiting approval. The edited plan is re-validated.
    /// </summary>
    public PlanValidationResult EditStep(string stepId, string intent, string? inputs)
    {
        if (Current is null) return new PlanValidationResult(false, new List<string> { "No plan to edit." });
        var steps = Current.Steps.Select(s => s.Id == stepId
            ? s with { Intent = intent, Inputs = inputs }
            : s).ToList();
        Current = Current with { Steps = steps };
        var result = new PlanValidator(AgentToolbox.KnownToolNames).Validate(Current);
        LastValidation = result;
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Runs the approved plan as an Ancora graph, streaming per-step status updates to the UI. Step
    /// statuses and outputs are captured live and exposed through <see cref="StepStatus"/> and
    /// <see cref="StepOutput"/>.
    /// </summary>
    public async Task<GraphRunResult> RunAsync(
        ProviderConfig provider,
        string model,
        IDatabaseAdapter adapter,
        int maxRows,
        CancellationToken ct = default)
    {
        if (Current is null) return new GraphRunResult(new PlanGraph(), false, "No plan to run.");
        State = PlanRunState.Running;
        _stepStatus.Clear();
        foreach (var step in Current.Steps)
        {
            _stepStatus[step.Id] = PlanStepStatus.Pending;
        }
        Changed?.Invoke();

        var progress = new Progress<GraphStepEvent>(ev =>
        {
            if (!string.IsNullOrEmpty(ev.StepId) && ev.StepId != "run_query")
            {
                _stepStatus[ev.StepId] = ev.Status;
                if (ev.Status == PlanStepStatus.Completed && ev.Detail is not null)
                {
                    _stepOutput[ev.StepId] = ev.Detail;
                }
            }
            Changed?.Invoke();
        });

        var result = await _graph.RunAsync(Current, provider, model, adapter, maxRows, progress, default, ct);
        Current = result.Plan;
        State = result.Succeeded ? PlanRunState.Done : PlanRunState.Failed;
        LastError = result.FailureReason;
        Changed?.Invoke();
        return result;
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
        State = PlanRunState.AwaitingApproval;
        _stepStatus.Clear();
        _stepOutput.Clear();
        if (graph is not null)
        {
            foreach (var step in graph.Steps)
            {
                _stepStatus[step.Id] = PlanStepStatus.Pending;
            }
        }
        Changed?.Invoke();
        return graph;
    }

    public void Clear()
    {
        Current = null;
        LastValidation = null;
        LastError = null;
        State = PlanRunState.Idle;
        _stepStatus.Clear();
        _stepOutput.Clear();
        _corrections.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Records an execution error encountered while running the plan (for example a connection failure)
    /// so the UI can surface it.
    /// </summary>
    public void SetRunError(string message)
    {
        LastError = message;
        State = PlanRunState.Failed;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records the correction attempts for a step so the UI can show the self-repair sub-thread. When the
    /// budget was exhausted, the plan moves to a failed state and the last error is surfaced for the user.
    /// </summary>
    public void RecordCorrection(string stepId, CorrectionOutcome outcome)
    {
        _corrections[stepId] = outcome.Attempts.ToList();
        if (outcome.BudgetExhausted)
        {
            State = PlanRunState.Failed;
            LastError = $"Correction budget exhausted for step {stepId}: {outcome.LastError}";
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Runs error-driven self-correction for a failing query on a step, using the live adapter and the
    /// cached schema, and records the attempts for the UI. Returns the outcome (success, final SQL, or
    /// budget exhausted with the last error for the user to resolve).
    /// </summary>
    public CorrectionOutcome TryCorrectStep(string stepId, string sql, QueryLantern.Adapters.IDatabaseAdapter adapter, QueryLantern.Adapters.SchemaModel schema, int maxAttempts = 3)
    {
        var corrector = new SelfCorrectionService(new QueryValidator(adapter), new QueryRepairer(schema));
        var outcome = corrector.Correct(sql, maxAttempts);
        RecordCorrection(stepId, outcome);
        return outcome;
    }
}
