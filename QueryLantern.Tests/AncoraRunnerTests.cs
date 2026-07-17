namespace QueryLantern.Tests;

using System;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Services;
using Xunit;

public class AncoraRunnerTests
{
    private static ProviderConfig TestConfig() => new("http://localhost:11434/v1", "ANCORA_API_KEY");

    [Fact]
    public void Runner_Run_Produces_RunHandle()
    {
        var runner = new AncoraRunner();
        var session = runner.Run(TestConfig(), "hy3", "Answer in one sentence.");
        Assert.NotNull(session.Handle);
        Assert.False(string.IsNullOrEmpty(session.Handle.RunId));
        session.Dispose();
    }

    [Fact]
    public void Runner_StreamAsync_Returns_AsyncEnumerable()
    {
        var runner = new AncoraRunner();
        var stream = runner.StreamAsync(TestConfig(), "hy3", "Answer in one sentence.");
        Assert.NotNull(stream);
    }

    [Fact]
    public void AgentSpec_Record_Supports_With()
    {
        var spec = new AgentSpec("hy3", "instructions", new System.Collections.Generic.List<ToolSpec>());
        var copy = spec with { Model = "other" };
        Assert.Equal("hy3", spec.Model);
        Assert.Equal("other", copy.Model);
    }

    [Fact(Skip = "Requires a live OpenAI compatible endpoint configured via ANCORA_SMOKE_URL and ANCORA_SMOKE_KEY.")]
    public async Task Runner_Live_Stream_Emits_Started_And_Completed()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ANCORA_SMOKE_URL") ?? "http://localhost:11434/v1";
        var authEnv = Environment.GetEnvironmentVariable("ANCORA_SMOKE_AUTH_ENV") ?? "ANCORA_API_KEY";
        var runner = new AncoraRunner();

        var sawStarted = false;
        var sawTokenOrCompleted = false;
        await foreach (var ev in runner.StreamAsync(new ProviderConfig(baseUrl, authEnv), "hy3", "Say hello in one word."))
        {
            switch (ev)
            {
                case StartedEvent:
                    sawStarted = true;
                    break;
                case TokenEvent or CompletedEvent:
                    sawTokenOrCompleted = true;
                    break;
            }
        }

        Assert.True(sawStarted);
        Assert.True(sawTokenOrCompleted);
    }
}
