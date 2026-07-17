namespace QueryLantern.Services;

using System.Collections.Generic;
using QueryLantern.Adapters;
using QueryLantern.Models;

/// <summary>
/// The record of one correction attempt: the SQL tried, whether it succeeded, and the error if not.
/// Surfaced to the UI as a visible sub-thread on the step.
/// </summary>
public sealed record CorrectionAttempt(string Sql, bool Succeeded, string? Error);

/// <summary>
/// The outcome of attempting to self-correct a failing query within the budget.
/// </summary>
public sealed record CorrectionOutcome(
    bool Succeeded,
    string? FinalSql,
    IReadOnlyList<CorrectionAttempt> Attempts,
    bool BudgetExhausted,
    string? LastError);

/// <summary>
/// Drives error-driven self-correction: when a SELECT fails validation or execution, the exact error and
/// the schema are fed back to produce a corrected query, which is retried. A bounded retry budget (default
/// 3) caps the attempts; once exhausted the run stops and asks the user, showing the last error.
/// </summary>
public sealed class SelfCorrectionService
{
    private readonly QueryValidator _validator;
    private readonly QueryRepairer _repairer;

    public SelfCorrectionService(QueryValidator validator, QueryRepairer repairer)
    {
        _validator = validator;
        _repairer = repairer;
    }

    /// <summary>
    /// Attempts to repair and re-validate <paramref name="sql"/> within the budget. Each attempt records
    /// the SQL and outcome. Stops early on success or when the budget is exhausted.
    /// </summary>
    public CorrectionOutcome Correct(string sql, int maxAttempts = 3)
    {
        var budget = new CorrectionBudget(maxAttempts);
        var attempts = new List<CorrectionAttempt>();
        var current = sql;

        // First validation of the original SQL.
        var first = _validator.Validate(current);
        if (first.IsValid)
        {
            attempts.Add(new CorrectionAttempt(current, true, null));
            return new CorrectionOutcome(true, current, attempts, false, null);
        }

        attempts.Add(new CorrectionAttempt(current, false, first.Error));

        while (!budget.Exhausted)
        {
            if (!budget.TryRecord()) break;
            current = _repairer.Repair(current, first.Error ?? "query failed");
            var check = _validator.Validate(current);
            if (check.IsValid)
            {
                attempts.Add(new CorrectionAttempt(current, true, null));
                return new CorrectionOutcome(true, current, attempts, false, null);
            }

            attempts.Add(new CorrectionAttempt(current, false, check.Error));
            first = check;
        }

        return new CorrectionOutcome(false, current, attempts, budget.Exhausted, first.Error);
    }
}
