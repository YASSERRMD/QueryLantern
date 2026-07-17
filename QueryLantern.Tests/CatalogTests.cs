namespace QueryLantern.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QueryLantern.Data;
using QueryLantern.Models;
using Xunit;

public class CatalogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ql_catalog_{Guid.NewGuid():N}.db");
    private readonly CatalogStore _catalog;

    public CatalogTests()
    {
        _catalog = new CatalogStore(_path);
    }

    [Fact]
    public async Task ConnectionProfile_CRUD_RoundTrips()
    {
        var repo = new ConnectionRepository(_catalog);
        var profile = new ConnectionProfile
        {
            Name = "Local PG",
            Engine = DatabaseEngine.Postgresql,
            Host = "localhost",
            Port = 5432,
            Database = "analytics",
            Username = "reader",
            SecretRef = "vault://pg-1",
            Options = "sslmode=require"
        };

        var id = await repo.InsertAsync(profile);
        Assert.True(id > 0);

        var fetched = await repo.GetAsync(id);
        Assert.NotNull(fetched);
        Assert.Equal("Local PG", fetched!.Name);
        Assert.Equal(DatabaseEngine.Postgresql, fetched.Engine);
        Assert.Equal(5432, fetched.Port);
        Assert.Equal("vault://pg-1", fetched.SecretRef);

        var updated = fetched with { Host = "db.internal" };
        await repo.UpdateAsync(updated);
        var after = await repo.GetAsync(id);
        Assert.Equal("db.internal", after!.Host);

        var all = await repo.ListAsync();
        Assert.Contains(all, p => p.Id == id);

        await repo.DeleteAsync(id);
        Assert.Null(await repo.GetAsync(id));
    }

    [Fact]
    public async Task ProviderProfile_CRUD_RoundTrips()
    {
        var repo = new ProviderRepository(_catalog);
        var profile = new ProviderProfile
        {
            Name = "Novita Hy3",
            Kind = ProviderKind.Novita,
            BaseUrl = "https://novita.ai/v1",
            ModelId = "tencent/hy3",
            KeyRef = "vault://novita-1"
        };

        var id = await repo.InsertAsync(profile);
        Assert.True(id > 0);

        var fetched = await repo.GetAsync(id);
        Assert.NotNull(fetched);
        Assert.Equal(ProviderKind.Novita, fetched!.Kind);
        Assert.Equal("tencent/hy3", fetched.ModelId);

        await repo.UpdateAsync(fetched with { ModelId = "tencent/hy3-pro" });
        var after = await repo.GetAsync(id);
        Assert.Equal("tencent/hy3-pro", after!.ModelId);

        await repo.DeleteAsync(id);
        Assert.Null(await repo.GetAsync(id));
    }

    public void Dispose()
    {
        _catalog.Dispose();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
