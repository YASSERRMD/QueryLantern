namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Threading.Tasks;
using QueryLantern.Models;
using QueryLantern.Tools;

/// <summary>
/// Drives question decomposition for the UI: splits a compound question into sub-questions, builds a
/// sub-plan for each, and later composes the independent sub-answers into a final synthesized answer.
/// The decomposition tree is exposed so the chat surface can render and collapse its branches.
/// </summary>
public sealed class DecompositionService
{
    private readonly DecomposeTool _decompose;
    private readonly PlannerTool _planner;
    private readonly PlanValidator _validator;

    public Decomposition? Current { get; private set; }

    public event Action? Changed;

    public DecompositionService(DecomposeTool decompose, PlannerTool planner)
    {
        _decompose = decompose;
        _planner = planner;
        _validator = new PlanValidator(AgentToolbox.KnownToolNames);
    }

    /// <summary>
    /// Decomposes the question and produces a sub-plan (a PlanGraph) for every sub-question. Each
    /// sub-plan is validated. The combined decomposition tree plus sub-plans is exposed on
    /// <see cref="Current"/>.
    /// </summary>
    public DecompositionResult DecomposeAndPlan(string question, string? schemaSummary = null)
    {
        var decomposition = _decompose.Build(question);
        var subPlans = new Dictionary<string, PlanGraph>(System.StringComparer.Ordinal);
        var allValid = true;
        foreach (var sq in decomposition.SubQuestions)
        {
            var plan = _planner.Build(sq.Text, schemaSummary);
            var ok = _validator.Validate(plan).IsValid;
            if (!ok) allValid = false;
            subPlans[sq.Id] = plan;
        }

        Current = decomposition;
        Changed?.Invoke();
        return new DecompositionResult(decomposition, subPlans, allValid);
    }

    /// <summary>
    /// Composes independent sub-answers into a final answer in sub-question order.
    /// </summary>
    public string Compose(IReadOnlyList<string> subAnswers) => _decompose.Compose(subAnswers);

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}

/// <summary>
/// The outcome of decomposing and planning a compound question: the decomposition tree, a sub-plan per
/// sub-question, and whether every sub-plan validated.
/// </summary>
public sealed record DecompositionResult(
    Decomposition Decomposition,
    IReadOnlyDictionary<string, PlanGraph> SubPlans,
    bool AllSubPlansValid);
