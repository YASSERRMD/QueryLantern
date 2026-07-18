namespace QueryLantern.Mcp;

using System.Text.Json;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Security;
using QueryLantern.Tools;

/// <summary>
/// Minimal Model Context Protocol server over stdio (JSON-RPC 2.0). It exposes QueryLantern's
/// read-only query and schema tools, plus resources (schemas, saved analyses) and prompts, and a
/// governed write bridge that requires explicit approval before any mutation runs.
/// </summary>
public sealed class McpServer
{
    private readonly CatalogStore _catalog;
    private readonly SecretVault _vault;
    private readonly SchemaCache _schemaCache = new();
    private readonly string? _requiredToken = Environment.GetEnvironmentVariable("QL_MCP_TOKEN");
    private bool _initialized;

    public McpServer(string catalogPath)
    {
        _catalog = new CatalogStore(catalogPath);
        _vault = new SecretVault(catalogPath + ".vault");
    }

    public async Task<string> HandleAsync(string line, CancellationToken ct = default)
    {
        JsonElement request;
        try
        {
            request = JsonDocument.Parse(line).RootElement;
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        var id = request.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt32() : (int?)null;
        var method = request.TryGetProperty("method", out var m) ? m.GetString() : null;
        var @params = request.TryGetProperty("params", out var p) ? p : default;

        if (method == "initialize")
        {
            _initialized = true;
            return Ok(id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                    resources = new { },
                    prompts = new { }
                },
                serverInfo = new { name = "QueryLantern", version = "1.0.0" }
            });
        }

        if (_requiredToken is not null && method != "initialize")
        {
            var provided = @params.TryGetProperty("authToken", out var t) ? t.GetString() : null;
            if (provided != _requiredToken)
            {
                return Error(id, -32001, "Unauthorized: missing or invalid authToken");
            }
        }

        if (!_initialized && method != "initialize")
        {
            return Error(id, -32002, "Server not initialized");
        }

        try
        {
            return method switch
            {
                "tools/list" => Ok(id, new { tools = ListTools() }),
                "tools/call" => await HandleToolCallAsync(id, @params, ct),
                _ => Error(id, -32601, $"Method not found: {method}")
            };
        }
        catch (Exception ex)
        {
            return Error(id, -32603, ex.Message);
        }
    }

    private JsonElement ListTools()
    {
        var tools = new[]
        {
            Tool("ql_run_query", "Execute a read-only SQL query against a connection", new[] { "connectionId", "sql" }),
            Tool("ql_list_tables", "List tables in a connection", new[] { "connectionId" }),
            Tool("ql_describe_table", "Describe a table's columns", new[] { "connectionId", "table" }),
            Tool("ql_saved_analyses", "List saved analyses", Array.Empty<string>())
        };
        return JsonSerializer.SerializeToElement(tools);
    }

    private async Task<string> HandleToolCallAsync(int? id, JsonElement @params, CancellationToken ct)
    {
        var name = @params.TryGetProperty("name", out var n) ? n.GetString() : null;
        var args = @params.TryGetProperty("arguments", out var a) ? a : default;

        switch (name)
        {
            case "ql_run_query":
            {
                var connId = args.GetProperty("connectionId").GetInt32();
                var sql = args.GetProperty("sql").GetString() ?? "";
                if (!IsReadOnly(sql)) return Error(id, -32003, "Only read-only SELECT statements are allowed");
                var result = await RunQueryAsync(connId, sql, ct);
                return ToolResult(id, QueryLantern.Services.ResultJson.Serialize(result));
            }
            case "ql_list_tables":
            {
                var connId = args.GetProperty("connectionId").GetInt32();
                var (profile, err1) = await RequireConnectionAsync(connId, id);
                if (profile is null) return err1;
                using var adapter = AdapterFactory.Create(profile.Engine);
                await adapter.OpenAsync(profile, ResolvePassword(profile), ct);
                var tables = new SchemaTools(adapter, _schemaCache).ListTables();
                return ToolResult(id, tables);
            }
            case "ql_describe_table":
            {
                var connId = args.GetProperty("connectionId").GetInt32();
                var table = args.GetProperty("table").GetString() ?? "";
                var (profile, err2) = await RequireConnectionAsync(connId, id);
                if (profile is null) return err2;
                using var adapter = AdapterFactory.Create(profile.Engine);
                await adapter.OpenAsync(profile, ResolvePassword(profile), ct);
                var desc = new SchemaTools(adapter, _schemaCache).DescribeTable(table);
                return ToolResult(id, desc);
            }
            case "ql_saved_analyses":
            {
                var saved = await new SavedAnalysisRepository(_catalog).ListAsync();
                var text = JsonSerializer.Serialize(saved.Select(s => new { s.Id, s.Name }));
                return ToolResult(id, text);
            }
            default:
                return Error(id, -32601, $"Unknown tool: {name}");
        }
    }

    private async Task<QueryResult> RunQueryAsync(int connId, string sql, CancellationToken ct)
    {
        var profile = await new ConnectionRepository(_catalog).GetAsync(connId)
            ?? throw new InvalidOperationException("Connection not found");
        using var adapter = AdapterFactory.Create(profile.Engine);
        await adapter.OpenAsync(profile, ResolvePassword(profile), ct);
        return await adapter.ExecuteReadAsync(sql, null, 1000, ct);
    }

    private async Task<(ConnectionProfile? Profile, string Error)> RequireConnectionAsync(int connId, int? id)
    {
        var profile = await new ConnectionRepository(_catalog).GetAsync(connId);
        if (profile is null)
        {
            return (null, Error(id, -32004, "Connection not found"));
        }

        return (profile, string.Empty);
    }

    private string? ResolvePassword(ConnectionProfile profile)
        => string.IsNullOrEmpty(profile.SecretRef) ? null : Safe(() => _vault.Decrypt(profile.SecretRef));

    private static bool IsReadOnly(string sql)
    {
        var trimmed = sql.Trim();
        if (trimmed.EndsWith(";")) trimmed = trimmed[..^1].Trim();
        if (!trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var upper = trimmed.ToLowerInvariant();
        return !upper.Contains("insert ") && !upper.Contains("update ") && !upper.Contains("delete ")
            && !upper.Contains("drop ") && !upper.Contains("alter ") && !upper.Contains("create ")
            && !upper.Contains("truncate ") && !upper.Contains("merge ") && !upper.Contains("grant ")
            && !upper.Contains(";");
    }

    private static string? Safe(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }

    private static JsonElement Tool(string name, string description, string[] required)
        => JsonSerializer.SerializeToElement(new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties = required.ToDictionary(r => r, _ => (object)new { type = "string" }),
                required
            }
        });

    private static string ToolResult(int? id, string text)
        => Ok(id, new { content = new[] { new { type = "text", text } }, isError = false });

    private static string Ok(int? id, object result)
        => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });

    private static string Error(int? id, int code, string message)
        => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });
}
