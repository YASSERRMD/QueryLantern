namespace QueryLantern.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Ancora;

/// <summary>
/// Wraps the Ancora Agent and Runtime lifecycle and exposes a streamed run that yields
/// StartedEvent, TokenEvent, and CompletedEvent (plus ToolCall, Suspended, Resumed, Failed).
/// </summary>
public sealed class AncoraRunner : IDisposable
{
    private readonly Runtime _runtime;
    private bool _disposed;

    public AncoraRunner(ProviderConfig provider)
    {
        _runtime = new Runtime(provider);
    }

    public AncoraRunner(string baseUrl, string authEnvVar, string chatCompletionsPath = "/v1/chat/completions")
        : this(new ProviderConfig(baseUrl, authEnvVar, chatCompletionsPath))
    {
    }

    /// <summary>
    /// Starts an agent run for the given model and instructions and returns the run handle.
    /// </summary>
    public RunHandle Run(string model, string instructions, AgentSpec? baseSpec = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var spec = baseSpec ?? new AgentSpec(model, instructions, new System.Collections.Generic.List<ToolSpec>());
        var agent = new Agent(_runtime);
        return agent.Run(spec);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Dispose();
    }
}
