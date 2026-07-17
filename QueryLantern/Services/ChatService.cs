namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Settings;
using QueryLantern.Tools;

/// <summary>
/// Orchestrates a single conversational run: opens the chosen connection, builds an Ancora runtime
/// with the governed tools, streams the assistant's tokens and tool calls into the chat log, and
/// pauses for human approval when a write is staged. Resuming (approve/reject) completes the run.
/// </summary>
public sealed class ChatService
{
    public enum ChatState { Idle, Running, AwaitingApproval, Done, Error }

    public sealed record ChatEntry(string Role, string Content, IReadOnlyList<string> ToolCalls);
    public sealed record PendingWrite(RunnerSession Session, IDatabaseAdapter Adapter, string Sql);

    private readonly SettingsService _settings;
    private readonly ModelRouter _router;
    private readonly AgentToolbox _toolbox;
    private readonly HumanInTheLoop _hitl;
    private readonly SchemaCache _schemaCache;
    private readonly ApprovalService _approval;
    private readonly ActivityJournal _journal;
    private readonly CostService _cost;

    public List<ChatEntry> Entries { get; } = new();
    public ChatState State { get; private set; } = ChatState.Idle;
    public PendingWrite? Pending { get; private set; }
    public string? LastCost { get; private set; }
    public string? LastError { get; private set; }
    public string? LastResultSetJson { get; private set; }

    private string _costProviderName = "unknown";
    private string _costModel = "unknown";

    public event Action? Changed;

    public ChatService(SettingsService settings, ModelRouter router, AgentToolbox toolbox, HumanInTheLoop hitl, SchemaCache schemaCache, ApprovalService approval, ActivityJournal journal, CostService cost)
    {
        _settings = settings;
        _router = router;
        _toolbox = toolbox;
        _hitl = hitl;
        _schemaCache = schemaCache;
        _approval = approval;
        _journal = journal;
        _cost = cost;
    }

    public void Reset()
    {
        Entries.Clear();
        State = ChatState.Idle;
        Pending = null;
        LastCost = null;
        LastError = null;
        LastResultSetJson = null;
        Changed?.Invoke();
    }

    public async Task SendAsync(string userMessage, int connectionId, int providerId, string? modelOverride = null, CancellationToken ct = default)
    {
        Entries.Add(new ChatEntry("user", userMessage, Array.Empty<string>()));
        var assistant = new ChatEntry("assistant", string.Empty, new List<string>());
        Entries.Add(assistant);
        State = ChatState.Running;
        LastError = null;
        Changed?.Invoke();

        try
        {
            var resolved = await _settings.ResolveConnectionAsync(connectionId);
            var adapter = AdapterFactory.Create(resolved.Profile.Engine);
            await adapter.OpenAsync(resolved.Profile, resolved.Password, ct);

            var (providerConfig, model) = await _router.ResolveAsync(providerId, modelOverride);
            var providerProfile = await _settings.GetProviderProfileAsync(providerId);
            _costProviderName = providerProfile?.Name ?? "unknown";
            _costModel = model;
            var runner = new AncoraRunner();
            List<ToolSpec> specs = new();
            var session = runner.StartRun(providerConfig, model, BuildInstructions(resolved.Profile),
                runtime => specs = _toolbox.RegisterTools(runtime, adapter, onRunQueryResult: sql =>
                {
                    LastResultSetJson = sql;
                    _journal.Append("query", sql);
                }),
                specs);

            await foreach (var ev in session.Handle.EventsAsync(ct))
            {
                switch (ev)
                {
                    case TokenEvent te:
                        assistant = assistant with { Content = assistant.Content + te.Text };
                        Changed?.Invoke();
                        break;
                    case ToolCallEvent tce:
                        ((List<string>)assistant.ToolCalls).Add($"{tce.Name}: {tce.Input}");
                        Changed?.Invoke();
                        break;
                    case SuspendedEvent se when se.ToolName == "propose_write":
                        var sql = ExtractSql(se.ArgumentsJson);
                        if (_approval.AutoRejectWrites)
                        {
                            await _hitl.RejectAsync(session, ct);
                            Finalize(session, adapter);
                            break;
                        }
                        if (!_approval.RequireApproval)
                        {
                            await _hitl.ApproveAsync(session, adapter, sql, ct);
                            Finalize(session, adapter);
                            break;
                        }
                        Pending = new PendingWrite(session, adapter, sql);
                        State = ChatState.AwaitingApproval;
                        Changed?.Invoke();
                        return; // wait for Approve/Reject
                    case CompletedEvent ce:
                        assistant = assistant with { Content = assistant.Content + (string.IsNullOrEmpty(assistant.Content) ? ce.Output : "") };
                        break;
                }
            }

            Finalize(session, adapter);
        }
        catch (Exception ex)
        {
            State = ChatState.Error;
            LastError = ex.Message;
            Changed?.Invoke();
        }
    }

    public async Task ApproveAsync(CancellationToken ct = default)
    {
        if (Pending is null) return;
        var pending = Pending;
        Pending = null;
        State = ChatState.Running;
        Changed?.Invoke();
        try
        {
            await _hitl.ApproveAsync(pending.Session, pending.Adapter, pending.Sql, ct);
            _journal.Append("write_approved", pending.Sql);
            Finalize(pending.Session, pending.Adapter);
        }
        catch (Exception ex)
        {
            State = ChatState.Error;
            LastError = ex.Message;
            Changed?.Invoke();
        }
    }

    public async Task RejectAsync(CancellationToken ct = default)
    {
        if (Pending is null) return;
        var pending = Pending;
        Pending = null;
        State = ChatState.Running;
        Changed?.Invoke();
        try
        {
            await _hitl.RejectAsync(pending.Session, ct);
            _journal.Append("write_rejected", pending.Sql);
            Finalize(pending.Session, pending.Adapter);
        }
        catch (Exception ex)
        {
            State = ChatState.Error;
            LastError = ex.Message;
            Changed?.Invoke();
        }
    }

    private void Finalize(RunnerSession session, IDatabaseAdapter adapter)
    {
        var total = (decimal?)(session.Handle.GetCostTyped()?.TotalUsd) ?? 0m;
        LastCost = total.ToString("C4");
        _cost.Record(session.Handle.RunId, _costProviderName, _costModel, total);
        adapter.Dispose();
        session.Dispose();
        State = ChatState.Done;
        Changed?.Invoke();
    }

    private static string BuildInstructions(ConnectionProfile profile)
        => $"You are QueryLantern, a data analyst assistant connected to a {profile.Engine} database named '{profile.Database}'. " +
           "Use run_query for read-only questions, list_tables/describe_table/sample_rows/explain_plan to explore the schema, " +
           "and propose_write only when the user explicitly asks to change data. Never invent table or column names.";

    private static string ExtractSql(string argumentsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("sql", out var el))
            {
                return el.GetString() ?? string.Empty;
            }
        }
        catch { }
        return argumentsJson;
    }
}
