# QueryLantern quick start

This guide walks through a fully local, air-gapped setup: a local SQLite database, a local model
endpoint, and a natural-language question.

## 1. Create a sample database

```bash
sqlite3 sample.db <<'SQL'
CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, country TEXT, signup DATE);
INSERT INTO customers VALUES
  (1, 'Ada',     'UK', '2024-01-04'),
  (2, 'Linus',   'FI', '2024-02-11'),
  (3, 'Grace',   'US', '2024-02-19'),
  (4, 'Margaret','US', '2024-03-02');
SQL
```

## 2. Start a local model endpoint

QueryLantern speaks the OpenAI-compatible `/v1/chat/completions` protocol. Any local server works:

```bash
# Ollama
ollama pull qwen2.5:7b
ollama serve   # listens on http://localhost:11434/v1

# or vLLM
python -m vllm.entrypoints.openai.api_server --model Qwen/Qwen2.5-7B-Instruct
```

## 3. Configure QueryLantern

```bash
dotnet run --project QueryLantern
```

1. **Providers** → Add. Pick kind `Ollama`, set the model id (e.g. `qwen2.5:7b`), leave the key blank.
2. **Connections** → Add. Engine `Sqlite`, database `sample.db`.
3. **Chat** → choose the connection and provider, then ask:

   > How many customers signed up per country in February 2024?

The agent introspects the schema, drafts a `SELECT`, runs it read-only through `run_query`, and
renders the result as a table and chart. A write such as "delete all UK customers" is staged by
`propose_write` and only runs after you click **Approve**.

## 4. Inspect what happened

- **Activity** shows the signed journal (queries run, writes approved/rejected).
- **Cost** shows estimated spend per run.
- **History** lets you save the conversation and reopen it later, or export the result as CSV.
