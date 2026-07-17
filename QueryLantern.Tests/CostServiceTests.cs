namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Services;
using Xunit;

public class CostServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"cost_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void Records_And_Aggregates()
    {
        var svc = new CostService(_file);
        svc.Record("r1", "OpenAI", "gpt-4o", 0.0123m);
        svc.Record("r2", "OpenAI", "gpt-4o", 0.0042m);

        Assert.Equal(2, svc.ReadAll().Count);
        Assert.Equal(0.0165m, svc.TotalUsd(), 4);
    }

    [Fact]
    public void Empty_When_No_Records()
    {
        var svc = new CostService(_file);
        Assert.Empty(svc.ReadAll());
        Assert.Equal(0m, svc.TotalUsd());
    }
}
