namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Adapters;
using QueryLantern.Data;
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
    private readonly SavedAnalysisRepository _saved;
    private readonly AnswerGroundingService _grounding;
    private readonly AnswerCriticService _critic;
    private readonly ConfidenceService _confidence;
    private readonly SchemaMemoryService _memory;
    private readonly ConversationMemoryService _conversation;
    private readonly SemanticLayerService _semantic;
    private readonly QueryLibraryService _library;
    private string? _lastSql;

    public List<ChatEntry> Entries { get; } = new();
    public ChatState State { get; private set; } = ChatState.Idle;
    public PendingWrite? Pending { get; private set; }
    public string? LastCost { get; private set; }
    public string? LastError { get; private set; }
    public string? LastResultSetJson { get; private set; }
    public string? LastAnswer { get; private set; }
    public GroundingResult? LastGrounding { get; private set; }
    public CritiqueResult? LastCritique { get; private set; }
    public ConfidenceScore? LastConfidence { get; private set; }
    public string? LastUserQuestion { get; private set; }
    public ResolvedQuestion? LastResolvedQuestion { get; private set; }

    private string _costProviderName = "unknown";
    private string _costModel = "unknown";
    private int _currentConnectionId;
    private int _currentProviderId;

    public event Action? Changed;

    public ChatService(SettingsService settings, ModelRouter router, AgentToolbox toolbox, HumanInTheLoop hitl, SchemaCache schemaCache, ApprovalService approval, ActivityJournal journal, CostService cost, SavedAnalysisRepository saved, AnswerGroundingService grounding, AnswerCriticService critic, ConfidenceService confidence, SchemaMemoryService memory, ConversationMemoryService conversation, SemanticLayerService semantic, QueryLibraryService library)
    {
        _settings = settings;
        _router = router;
        _toolbox = toolbox;
        _hitl = hitl;
        _schemaCache = schemaCache;
        _approval = approval;
        _journal = journal;
        _cost = cost;
        _saved = saved;
        _grounding = grounding;
        _critic = critic;
        _confidence = confidence;
        _memory = memory;
        _conversation = conversation;
        _semantic = semantic;
        _library = library;
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

    /// <summary>
    /// Persists the current conversation as a named analysis so it can be reopened later.
    /// </summary>
    public async Task<int> SaveAsync(string name)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new SavedAnalysisPayload(
            _currentConnectionId, _currentProviderId, Entries, LastResultSetJson));
        return await _saved.InsertAsync(new SavedAnalysis(0, name, payload, DateTime.UtcNow));
    }

    /// <summary>
    /// Loads a previously saved conversation into the active chat surface.
    /// </summary>
    public void LoadFromPayload(string payload)
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<SavedAnalysisPayload>(payload);
        if (data is null) return;
        Entries.Clear();
        Entries.AddRange(data.Entries);
        _currentConnectionId = data.ConnectionId;
        _currentProviderId = data.ProviderId;
        LastResultSetJson = data.ResultJson;
        State = ChatState.Done;
        Changed?.Invoke();
    }

    public sealed record SavedAnalysisPayload(int ConnectionId, int ProviderId, List<ChatEntry> Entries, string? ResultJson);

    public async Task SendAsync(string userMessage, int connectionId, int providerId, string? modelOverride = null, CancellationToken ct = default)
    {
        _currentConnectionId = connectionId;
        _currentProviderId = providerId;
        Entries.Add(new ChatEntry("user", userMessage, Array.Empty<string>()));
        var resolvedQ = _conversation.Resolve(userMessage);
        LastUserQuestion = resolvedQ.Resolved;
        LastResolvedQuestion = resolvedQ;
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
            var session = runner.StartRun(providerConfig, model, await BuildInstructions(connectionId, resolved.Profile),
                runtime => specs = _toolbox.RegisterTools(runtime, adapter, onRunQueryResult: sql =>
                {
                    LastResultSetJson = sql;
                    _journal.Append("query", sql);
                }, onRunQuerySql: s => _lastSql = s),
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
                        LastAnswer = assistant.Content;
                        LastGrounding = ComputeGrounding(assistant.Content);
                        LastCritique = _critic.Critique(LastUserQuestion ?? "", assistant.Content, LastGrounding);
                        LastConfidence = _confidence.Compute(LastGrounding, LastCritique, false, 0);
                        Changed?.Invoke();
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
        if (!string.IsNullOrEmpty(LastAnswer))
        {
            LastGrounding = ComputeGrounding(LastAnswer);
            LastCritique = _critic.Critique(LastUserQuestion, LastAnswer, LastGrounding);
            LastConfidence = _confidence.Compute(LastGrounding, LastCritique, false, 0);
            if (LastConfidence.Level == ConfidenceLevel.High && !string.IsNullOrEmpty(_lastSql) && _currentConnectionId > 0)
            {
                _ = _library.SaveAsync(_currentConnectionId, LastUserQuestion ?? "", _lastSql, 1);
            }
        }

        if (!string.IsNullOrEmpty(LastUserQuestion))
        {
            var summary = string.IsNullOrEmpty(LastResultSetJson)
                ? "(no result)"
                : LastResultSetJson.Length > 200 ? LastResultSetJson[..200] : LastResultSetJson;
            _conversation.Record(LastUserQuestion, string.Empty, summary);
        }
        var total = (decimal?)(session.Handle.GetCostTyped()?.TotalUsd) ?? 0m;
        LastCost = total.ToString("C4");
        _cost.Record(session.Handle.RunId, _costProviderName, _costModel, total);
        adapter.Dispose();
        session.Dispose();
        State = ChatState.Done;
        Changed?.Invoke();
    }

    private GroundingResult ComputeGrounding(string answer)
    {
        var results = new List<QueryResult>();
        if (!string.IsNullOrEmpty(LastResultSetJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(LastResultSetJson);
                var root = doc.RootElement;
                var columns = new List<ColumnMeta>();
                if (root.TryGetProperty("columns", out var cols))
                {
                    foreach (var c in cols.EnumerateArray())
                    {
                        var name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var type = c.TryGetProperty("dataType", out var t) ? t.GetString() ?? "" : "";
                        columns.Add(new ColumnMeta(name, type));
                    }
                }

                var rows = new List<IReadOnlyList<object?>>();
                if (root.TryGetProperty("rows", out var rs))
                {
                    foreach (var r in rs.EnumerateArray())
                    {
                        var row = new List<object?>();
                        foreach (var v in r.EnumerateArray())
                        {
                            row.Add(v.ValueKind == System.Text.Json.JsonValueKind.Null ? null : v.ToString());
                        }
                        rows.Add(row);
                    }
                }

                results.Add(new QueryResult { Columns = columns, Rows = rows, TruncatedAt = rows.Count });
            }
            catch { }
        }

        return _grounding.Check(answer, results);
    }

    private async Task<string> BuildInstructions(int connectionId, ConnectionProfile profile)
    {
        var baseText = $"You are QueryLantern, a data analyst assistant connected to a {profile.Engine} database named '{profile.Database}'. " +
           "Use run_query for read-only questions, list_tables/describe_table/sample_rows/explain_plan to explore the schema, " +
           "and propose_write only when the user explicitly asks to change data. Never invent table or column names.";
        var memory = await _memory.SummarizeForPromptAsync(connectionId);
        var glossary = await _semantic.BuildGlossaryAsync(connectionId);
        var fewShot = await _library.BuildFewShotAsync(connectionId, LastUserQuestion ?? string.Empty);
        var extra = string.Join("\n", new[] { memory, glossary, fewShot }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(extra) ? baseText : baseText + "\n" + extra;
    }

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
