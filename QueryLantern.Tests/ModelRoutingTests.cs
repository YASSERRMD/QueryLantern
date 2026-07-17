namespace QueryLantern.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Ancora;
using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Security;
using QueryLantern.Services;
using QueryLantern.Settings;
using Xunit;

public class ModelRoutingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ql_route_{Guid.NewGuid():N}.db");
    private readonly string _key = Path.Combine(Path.GetTempPath(), $"ql_route_key_{Guid.NewGuid():N}.key");
    private readonly CatalogStore _catalog;
    private readonly SettingsService _settings;
    private readonly ModelRouter _router;

    public ModelRoutingTests()
    {
        _catalog = new CatalogStore(_path);
        var vault = new SecretVault(_key);
        _settings = new SettingsService(new ConnectionRepository(_catalog), new ProviderRepository(_catalog), vault);
        _router = new ModelRouter(_settings);
    }

    [Fact]
    public async Task Resolve_Uses_Profile_Endpoint_And_Model()
    {
        var repo = new ProviderRepository(_catalog);
        var id = await repo.InsertAsync(new ProviderProfile
        {
            Name = "Novita",
            Kind = ProviderKind.Novita,
            BaseUrl = "https://novita.ai/v1",
            ModelId = "tencent/hy3"
        });

        var (config, model) = await _router.ResolveAsync(id);
        Assert.Equal("https://novita.ai/v1", config.BaseUrl);
        Assert.Equal("tencent/hy3", model);
    }

    [Fact]
    public async Task Resolve_Honors_PerConversation_ModelOverride()
    {
        var repo = new ProviderRepository(_catalog);
        var id = await repo.InsertAsync(new ProviderProfile
        {
            Name = "OpenAI",
            Kind = ProviderKind.OpenAI,
            BaseUrl = "https://api.openai.com/v1",
            ModelId = "gpt-4o"
        });

        var (config, model) = await _router.ResolveAsync(id, modelOverride: "gpt-4o-mini");
        Assert.Equal("gpt-4o-mini", model);
        Assert.Equal("https://api.openai.com/v1", config.BaseUrl);
    }

    public void Dispose()
    {
        _catalog.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_key)) File.Delete(_key);
    }
}
