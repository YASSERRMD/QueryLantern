# QueryLantern

QueryLantern is an open source conversational SQL and data analyst agent. You chat with your
relational databases in natural language. The agent inspects the schema, drafts SQL, executes
read only queries through a governed tool, streams its interpretation token by token, and renders
charts from the result set. Any statement that writes or changes structure is blocked at the tool
boundary and requires explicit human approval before it runs.

QueryLantern is the flagship showcase of the **Ancora** agentic framework. Every major Ancora
capability is exercised as a first class product feature: streaming events, delegate and attribute
tools, human in the loop resume, journaled activities, Ed25519 identity, cost summaries, local first
defaults, and multi provider model routing.

## Architecture summary

- **UI:** Blazor (interactive Server render mode, .NET 9), one project, component driven.
- **Agent core:** `Yasserrmd.Ancora` (>= 0.1.2). Native libraries ship in the package.
- **Data access:** ADO.NET provider abstraction behind a per engine `IDatabaseAdapter`. No heavyweight ORM.
- **Charts:** a Blazor friendly charting library rendered from query result sets.
- **Config store:** a local SQLite catalog for saved connections and provider profiles (secrets encrypted at rest).
- **Target framework:** net9.0.

### Database engines

PostgreSQL, MySQL or MariaDB, Microsoft SQL Server, Oracle Database (12c and later), SQLite,
DuckDB, ClickHouse, and a generic ODBC fallback. Each engine sits behind `IDatabaseAdapter`.

### LLM providers

Runtime configurable OpenAI compatible provider profiles: Novita, OpenRouter, OpenAI, Azure OpenAI,
local vLLM, local Ollama, NVIDIA NIM, and a generic custom endpoint. The agent model is chosen per
conversation from a saved profile.

## Run instructions

```bash
dotnet run --project QueryLantern
```

Then open the printed localhost URL. Use the Connections page to add a database, the Providers page
to add an OpenAI compatible model profile, then start a chat.

## License

Apache License 2.0. See the [LICENSE](LICENSE) file for details.
