namespace QueryLantern.Services;

using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Tools;

/// <summary>
/// Drives the human in the loop gate for mutating statements. propose_write stages a statement and
/// suspends the run; this service resumes it with an explicit decision. On approval the staged
/// statement is executed through the adapter exactly once, then the run continues with the result.
/// On rejection the run is cancelled cleanly with an error decision.
/// </summary>
public sealed class HumanInTheLoop
{
    /// <summary>
    /// Approves a staged write: executes the statement, then resumes the run with the outcome so the
    /// agent can report it.
    /// </summary>
    public async Task ApproveAsync(RunnerSession session, IDatabaseAdapter adapter, string stagedSql, CancellationToken ct = default)
    {
        var rows = new AgentToolbox().ExecuteApprovedWrite(adapter, stagedSql);
        await session.Handle.ResumeAndCollectAsync($"approved: {rows} row(s) affected", ct);
    }

    /// <summary>
    /// Rejects a staged write: resumes the run with an error decision so the agent stops and reports
    /// the rejection without executing anything.
    /// </summary>
    public async Task RejectAsync(RunnerSession session, CancellationToken ct = default)
    {
        await session.Handle.ResumeAndCollectAsync("rejected", ct);
    }
}
