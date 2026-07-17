namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using QueryLantern.Models;

/// <summary>
/// Computes a dependency-ordered execution sequence for a PlanGraph. Used to drive the graph runner and
/// to assert ordering in tests. The order is stable: steps are emitted after all of their dependencies.
/// </summary>
public static class PlanOrder
{
    /// <summary>
    /// Returns the step ids in a valid topological order. Throws if the plan contains a cycle (it should
    /// have been validated first).
    /// </summary>
    public static IReadOnlyList<string> TopologicalOrder(PlanGraph plan)
    {
        var byId = plan.Steps.ToDictionary(s => s.Id, s => s, System.StringComparer.Ordinal);
        var visited = new Dictionary<string, int>(); // 0=unvisited, 1=in progress, 2=done
        var order = new List<string>();

        foreach (var step in plan.Steps)
        {
            Visit(step.Id);
        }

        return order;

        void Visit(string id)
        {
            if (visited.TryGetValue(id, out var state))
            {
                if (state == 2) return;
                if (state == 1) throw new System.InvalidOperationException($"Cycle detected at step '{id}'.");
            }

            visited[id] = 1;
            if (byId.TryGetValue(id, out var step))
            {
                foreach (var dep in step.DependsOn.Where(byId.ContainsKey))
                {
                    Visit(dep);
                }
            }

            visited[id] = 2;
            order.Add(id);
        }
    }
}
