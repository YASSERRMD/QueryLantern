namespace QueryLantern.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ancora;

/// <summary>
/// Wraps the Ancora Agent and Runtime lifecycle and exposes streamed runs bound to a specific
/// provider profile. Each run creates its own Runtime from the supplied provider config so
/// different conversations can target different OpenAI compatible endpoints and models.
/// </summary>
public sealed class AncoraRunner
{
    /// <summary>
    /// Starts a run against the given provider config and model, returning the run handle. The
    /// caller owns the runtime lifetime and must dispose the returned <see cref="RunHandle"/> wrapper.
    /// </summary>
    public RunnerSession Run(ProviderConfig provider, string model, string instructions, AgentSpec? baseSpec = null)
    {
        var spec = baseSpec ?? new AgentSpec(model, instructions, new List<ToolSpec>());
        var runtime = new Runtime(provider);
        var agent = new Agent(runtime);
        var handle = agent.Run(spec);
        return new RunnerSession(runtime, handle);
    }

    /// <summary>
    /// Starts a run and streams its events: StartedEvent, TokenEvent, ToolCallEvent, SuspendedEvent,
    /// ResumedEvent, FailedEvent, and CompletedEvent. The runtime is created for the run and disposed
    /// once the stream completes. The optional <paramref name="registerTools"/> callback registers
    /// governed tools on the runtime before the run starts.
    /// </summary>
    public async IAsyncEnumerable<RunEvent> StreamAsync(
        ProviderConfig provider,
        string model,
        string instructions,
        Action<Runtime>? registerTools = null,
        IReadOnlyList<ToolSpec>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var runtime = new Runtime(provider);
        registerTools?.Invoke(runtime);
        var spec = new AgentSpec(model, instructions, tools is null ? new List<ToolSpec>() : tools.ToList());
        var agent = new Agent(runtime);
        var handle = agent.Run(spec);
        await foreach (var ev in handle.EventsAsync(cancellationToken))
        {
            yield return ev;
        }
    }
}

/// <summary>
/// Pairs a run handle with the runtime that backs it so disposing this disposes both. This keeps the
/// native runtime alive for the whole run and releases it afterwards.
/// </summary>
public sealed class RunnerSession : IDisposable
{
    private readonly Runtime _runtime;
    public RunHandle Handle { get; }

    public RunnerSession(Runtime runtime, RunHandle handle)
    {
        _runtime = runtime;
        Handle = handle;
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }
}
