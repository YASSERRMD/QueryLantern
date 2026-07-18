# QueryLantern MCP Server

A minimal [Model Context Protocol](https://modelcontextprotocol.io) server that exposes
QueryLantern's database capabilities over stdio using JSON-RPC 2.0. It lets MCP clients
(editors, agents) query and explore configured databases, read saved analyses, and run
governed writes that require explicit human approval.

## Build

```bash
dotnet build QueryLantern.Mcp/QueryLantern.Mcp.csproj
```

## Run

```bash
# Share the same catalog as the web app so connections are available:
export CATALOG_PATH=catalog.db
dotnet run --project QueryLantern.Mcp
```

The server reads one JSON-RPC request per line on stdin and writes one JSON response per line
on stdout. For example, using `nc` or any stdio MCP client:

```
{"jsonrpc":"2.0","id":1,"method":"initialize"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ql_run_query","arguments":{"connectionId":1,"sql":"SELECT * FROM orders LIMIT 10"}}}
```

## Tools

| Tool | Purpose |
| --- | --- |
| `ql_run_query` | Execute a **read-only** SQL query (mutations are rejected). |
| `ql_list_tables` | List tables in a connection. |
| `ql_describe_table` | Describe a table's columns. |
| `ql_saved_analyses` | List saved analyses. |
| `ql_propose_write` | Stage a mutating statement; returns an approval ticket. |
| `ql_approve_write` | Approve and execute a staged write using its ticket (human-in-the-loop). |

## Resources

- `ql://schema/{connectionId}` — the introspected schema of a connection.
- `ql://saved/{id}` — a saved analysis payload.

## Prompts

- `ql_analysis` — a template that guides analysis of a connection.

## Authentication / hardening

Set `QL_MCP_TOKEN` to require an `authToken` argument on every request (except `initialize`):

```bash
export QL_MCP_TOKEN=super-secret
```

Every request is validated: read-only queries reject `INSERT/UPDATE/DELETE/DROP/...`, the
write bridge never executes without an explicit `ql_approve_write` call, and unknown tools,
resources and methods return standard JSON-RPC errors.
