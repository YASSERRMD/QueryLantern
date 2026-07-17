namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using QueryLantern.Models;
using QueryLantern.Tools;

/// <summary>
/// Validates a PlanGraph before it is executed. A valid plan has no dependency cycles, references only
/// known tools, and resolves every declared dependency to a real step.
/// </summary>
public sealed class PlanValidator
{
    private readonly IReadOnlySet<string> _knownTools;

    public PlanValidator(IEnumerable<string> knownTools)
    {
        _knownTools = new HashSet<string>(knownTools, System.StringComparer.Ordinal);
    }

    /// <summary>
    /// Validates the plan and returns a result describing whether it is valid and, if not, every reason
    /// it failed. The planner tool "answer" is always considered a known terminal tool.
    /// </summary>
    public PlanValidationResult Validate(PlanGraph plan)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(plan.Steps.Select(s => s.Id), System.StringComparer.Ordinal);

        if (plan.Steps.Count == 0)
        {
            errors.Add("Plan has no steps.");
        }

        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add("A step has an empty id.");
            }

            if (!_knownTools.Contains(step.Tool) && !string.Equals(step.Tool, "answer", System.StringComparison.Ordinal))
            {
                errors.Add($"Step '{step.Id}' references unknown tool '{step.Tool}'.");
            }

            foreach (var dep in step.DependsOn)
            {
                if (!ids.Contains(dep))
                {
                    errors.Add($"Step '{step.Id}' depends on unknown step '{dep}'.");
                }

                if (string.Equals(dep, step.Id, System.StringComparison.Ordinal))
                {
                    errors.Add($"Step '{step.Id}' depends on itself.");
                }
            }
        }

        errors.AddRange(DetectCycles(plan, ids));

        return new PlanValidationResult(errors.Count == 0, errors);
    }

    private static IEnumerable<string> DetectCycles(PlanGraph plan, HashSet<string> ids)
    {
        var byId = plan.Steps.ToDictionary(s => s.Id, s => s, System.StringComparer.Ordinal);
        var state = ids.ToDictionary(id => id, _ => 0); // 0=unvisited, 1=in progress, 2=done

        foreach (var start in plan.Steps)
        {
            if (state[start.Id] == 0)
            {
                var stack = new Stack<string>();
                if (Visit(start.Id))
                {
                    yield return $"Dependency cycle detected involving: {string.Join(" -> ", stack.Reverse())}";
                }

                bool Visit(string id)
                {
                    stack.Push(id);
                    state[id] = 1;
                    if (byId.TryGetValue(id, out var step))
                    {
                        foreach (var dep in step.DependsOn)
                        {
                            if (!ids.Contains(dep)) continue;
                            if (state[dep] == 1) return true;
                            if (state[dep] == 0 && Visit(dep)) return true;
                        }
                    }

                    state[id] = 2;
                    stack.Pop();
                    return false;
                }
            }
        }
    }
}

/// <summary>
/// The outcome of validating a plan: whether it is valid and the list of reasons it is not.
/// </summary>
public sealed record PlanValidationResult(bool IsValid, IReadOnlyList<string> Errors);
