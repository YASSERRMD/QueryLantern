namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using QueryLantern.Adapters;
using QueryLantern.Models;

/// <summary>
/// Orchestrates the read queries of a single analysis. A later query can consume the result set of an
/// earlier step (for example, the row ids produced by step 1 filter step 2). This stage builds the
/// ordered query list and resolves filter values from prior results; caps and deduplication are applied
/// by a companion method so each concern is auditable.
/// </summary>
public sealed class OrchestrationService
{
    private readonly int _maxQueries;
    private readonly int _maxRowsTotal;

    public OrchestrationService(int maxQueries = 12, int maxRowsTotal = 20000)
    {
        _maxQueries = maxQueries;
        _maxRowsTotal = maxRowsTotal;
    }

    /// <summary>
    /// Resolves the filter values a query should use from the result sets of its source steps (the first
    /// column of each prior row, for example a row id). These values are then available to the query
    /// execution layer to bind a parameterized filter.
    /// </summary>
    public IReadOnlyList<object?> ResolveFilterValues(
        AnalysisQuery query,
        IReadOnlyDictionary<string, QueryResult> priorResults)
    {
        var values = new List<object?>();
        foreach (var sourceId in query.SourceStepIds)
        {
            if (priorResults.TryGetValue(sourceId, out var result))
            {
                foreach (var row in result.Rows)
                {
                    if (row.Count > 0)
                    {
                        values.Add(row[0]);
                    }
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Builds the ordered execution query list, attaching resolved filter values from prior step result
    /// sets to each query.
    /// </summary>
    public IReadOnlyList<AnalysisQuery> ResolveChain(
        IReadOnlyList<AnalysisQuery> requested,
        IReadOnlyDictionary<string, QueryResult> priorResults)
    {
        return requested.Select(q => q with { FilterValues = ResolveFilterValues(q, priorResults) }).ToList();
    }

    /// <summary>
    /// Enforces the cap on the total number of rows pulled across all executed result sets, returning
    /// the capped total and a flag indicating whether a result was truncated.
    /// </summary>
    public (int TotalRows, bool Capped) TallyRows(IReadOnlyList<QueryResult> results)
    {
        var total = 0;
        var capped = false;
        foreach (var r in results)
        {
            total += r.RowCount;
            if (total > _maxRowsTotal)
            {
                capped = true;
                total = _maxRowsTotal;
                break;
            }
        }

        return (total, capped);
    }
}
