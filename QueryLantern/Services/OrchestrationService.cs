namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Linq;
using QueryLantern.Adapters;
using QueryLantern.Models;

/// <summary>
/// Orchestrates the read queries of a single analysis: it deduplicates identical SQL, enforces a cap on
/// the number of queries and the total rows pulled, and resolves the filter values a later query should
/// use from the result set of an earlier step (for example, passing row ids forward).
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
    /// Builds the execution query plan for an analysis. <paramref name="requested"/> lists the queries in
    /// order; <paramref name="priorResults"/> maps a step id to the result set it produced so a later
    /// query can draw filter values from it.
    /// </summary>
    public QueryPlan BuildPlan(
        IReadOnlyList<AnalysisQuery> requested,
        IReadOnlyDictionary<string, QueryResult> priorResults)
    {
        var queries = new List<AnalysisQuery>();
        var dropped = new List<string>();
        var seenSql = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var q in requested)
        {
            if (queries.Count >= _maxQueries)
            {
                dropped.Add($"Dropped query for step {q.StepId}: exceeded max queries ({_maxQueries}).");
                continue;
            }

            if (!seenSql.Add(Normalize(q.Sql)))
            {
                dropped.Add($"Dropped query for step {q.StepId}: duplicate of an earlier identical query.");
                continue;
            }

            var filterValues = ResolveFilterValues(q, priorResults);
            queries.Add(q with { FilterValues = filterValues });
        }

        return new QueryPlan(queries, _maxRowsTotal, dropped);
    }

    /// <summary>
    /// Enforces the total row cap across all executed result sets, returning the capped total and a flag
    /// indicating whether any result was truncated.
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

    private static IReadOnlyList<object?> ResolveFilterValues(
        AnalysisQuery query,
        IReadOnlyDictionary<string, QueryResult> priorResults)
    {
        var values = new List<object?>();
        foreach (var sourceId in query.SourceStepIds)
        {
            if (priorResults.TryGetValue(sourceId, out var result))
            {
                // Use the first column of each row as the filter value (for example a row id).
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

    private static string Normalize(string sql) => (sql ?? string.Empty).Trim().ToLowerInvariant();
}
