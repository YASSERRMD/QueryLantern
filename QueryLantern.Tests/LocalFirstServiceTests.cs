namespace QueryLantern.Tests;

using Microsoft.Extensions.Configuration;
using QueryLantern.Services;
using Xunit;

public class LocalFirstServiceTests
{
    private static LocalFirstService Create(bool enabled) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalFirst:Enabled"] = enabled ? "true" : "false"
        }).Build());

    [Fact]
    public void Blocks_External_Host_When_Enabled()
    {
        var svc = Create(true);
        Assert.Null(svc.RejectReasonIfBlocked("http://localhost:11434/v1"));
        Assert.Null(svc.RejectReasonIfBlocked("http://192.168.1.10:8000/v1"));
        Assert.NotNull(svc.RejectReasonIfBlocked("https://api.openai.com/v1"));
    }

    [Fact]
    public void Allows_External_When_Disabled()
    {
        var svc = Create(false);
        Assert.Null(svc.RejectReasonIfBlocked("https://api.openai.com/v1"));
    }

    [Fact]
    public void Rejects_Invalid_Uri()
    {
        var svc = Create(true);
        Assert.NotNull(svc.RejectReasonIfBlocked("not-a-url"));
    }
}
