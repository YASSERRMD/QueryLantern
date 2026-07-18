namespace QueryLantern.Tests;

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QueryLantern.Adapters;
using QueryLantern.Data;
using QueryLantern.Mcp;
using QueryLantern.Models;
using Xunit;

/// <summary>
/// Verifies the MCP server speaks JSON-RPC: initialize, tools/list, read-only query execution,
/// resources, prompts, and the governed write bridge (propose then approve) without network.
/// </summary>
public class McpServerTests : System.IDisposable
{
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), $"ql_mcp_{System.Guid.NewGuid():N}.db");
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"ql_mcpD_{System.Guid.NewGuid():N}.db");

    public McpServerTests()
    {
        var adapter = new SqliteAdapter();
        adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _data }, null).GetAwaiter().GetResult();
        adapter.ExecuteWriteAsync("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1),(2);").GetAwaiter().GetResult();

        var repo = new ConnectionRepository(new CatalogStore(_catalog));
        repo.InsertAsync(new ConnectionProfile { Name = "D", Engine = DatabaseEngine.Sqlite, Host = "", Port = 0, Database = _data, Username = "", SecretRef = "", Options = "" }).GetAwaiter().GetResult();
    }

    private McpServer Server() => new(_catalog);

    private JsonElement Rpc(string method, object? @params = null)
    {
        var server = Server();
        server.HandleAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 0, method = "initialize" })).GetAwaiter().GetResult();
        var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params });
        var resp = server.HandleAsync(json).GetAwaiter().GetResult();
        return JsonDocument.Parse(resp).RootElement;
    }

    [Fact]
    public void Initialize_Returns_Capabilities()
    {
        var el = Rpc("initialize");
        Assert.Equal("2.0", el.GetProperty("jsonrpc").GetString());
        Assert.True(el.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public void Tools_List_Exposes_QueryLantern_Tools()
    {
        var el = Rpc("tools/list");
        var tools = el.GetProperty("result").GetProperty("tools").EnumerateArray();
        var names = System.Linq.Enumerable.Select(tools, t => t.GetProperty("name").GetString()).ToArray();
        Assert.Contains("ql_run_query", names);
        Assert.Contains("ql_propose_write", names);
    }

    [Fact]
    public void Run_Query_Returns_Rows()
    {
        var el = Rpc("tools/call", new { name = "ql_run_query", arguments = new { connectionId = 1, sql = "SELECT * FROM t" } });
        var text = el.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("\"rowCount\":2", text!);
    }

    [Fact]
    public void Mutating_Query_Is_Rejected()
    {
        var el = Rpc("tools/call", new { name = "ql_run_query", arguments = new { connectionId = 1, sql = "DELETE FROM t" } });
        Assert.Equal(-32003, el.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Governed_Write_Requires_Approval()
    {
        var server = Server();
        server.HandleAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 0, method = "initialize" })).GetAwaiter().GetResult();
        var proposed = JsonDocument.Parse(server.HandleAsync(JsonSerializer.Serialize(new {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "ql_propose_write", arguments = new { connectionId = 1, sql = "INSERT INTO t VALUES (99)" } }
        })).GetAwaiter().GetResult()).RootElement;
        var ticket = JsonDocument.Parse(proposed.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!)
            .RootElement.GetProperty("ticket").GetString();
        Assert.False(string.IsNullOrEmpty(ticket));

        var approved = JsonDocument.Parse(server.HandleAsync(JsonSerializer.Serialize(new {
            jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "ql_approve_write", arguments = new { ticket } }
        })).GetAwaiter().GetResult()).RootElement;
        var status = JsonDocument.Parse(approved.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!)
            .RootElement.GetProperty("status").GetString();
        Assert.Equal("executed", status);
    }

    [Fact]
    public void Resources_And_Prompts_Are_Listed()
    {
        var res = Rpc("resources/list");
        Assert.True(res.GetProperty("result").GetProperty("resources").GetArrayLength() >= 1);
        var prompts = Rpc("prompts/list");
        Assert.Contains("ql_analysis", prompts.GetProperty("result").GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());
    }

    public void Dispose()
    {
        foreach (var f in new[] { _catalog, _data, _catalog + ".vault" })
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
