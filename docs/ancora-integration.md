# Ancora integration (Phase 1)

QueryLantern talks to language models through the `Yasserrmd.Ancora` package. This document
describes the wrapper we built and the event model the rest of the app streams from.

## AncoraRunner

`QueryLantern.Services.AncoraRunner` owns a single `Ancora.Runtime` for the process lifetime and
exposes two entry points:

- `RunHandle Run(string model, string instructions, AgentSpec? baseSpec = null)`
  Starts an agent run with the given model id and system instructions. Returns a `RunHandle` you
  can stream events from or collect a final answer from.
- `IAsyncEnumerable<RunEvent> StreamAsync(string model, string instructions, AgentSpec? baseSpec = null, CancellationToken ct = default)`
  Starts a run and yields every event as it arrives, so a caller can render streaming output.

The runner is registered as a singleton in `Program.cs` and reads its provider endpoint from
configuration (`Ancora:BaseUrl`, `Ancora:AuthEnvVar`, `Ancora:ChatCompletionsPath`). Later phases
replace this with a per conversation provider profile.

## Event model

`RunHandle.EventsAsync()` yields `Ancora.RunEvent`, a discriminated event with a `Kind` string.
The concrete subtypes the app switches on:

| Event | Fields | Meaning |
| --- | --- | --- |
| `StartedEvent` | `RunId`, `Spec` | The run has started. |
| `TokenEvent` | `RunId`, `Text` | One streamed token of the answer. |
| `ToolCallEvent` | `RunId`, `Name`, `Input` | The agent invoked a tool. |
| `SuspendedEvent` | `RunId`, `ToolCallId`, `ToolName`, `ArgumentsJson`, `Prompt` | The run paused for human approval. |
| `ResumedEvent` | `RunId`, `Decision` | The run resumed after a decision. |
| `FailedEvent` | `RunId`, `Error` | The run failed. |
| `CompletedEvent` | `RunId`, `Output` | The run finished with a final answer. |

## Cost and resume

`RunHandle.GetCost()` returns a JSON cost summary; `GetCostTyped()` returns a `Cost` record with
`RunId` and `TotalUsd`. `ResumeAndCollectAsync(decision)` resumes a suspended run after a human
approval decision. These are exercised in later phases (cost panel, human in the loop gate).
