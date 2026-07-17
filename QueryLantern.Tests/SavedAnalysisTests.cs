namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Data;
using QueryLantern.Tools;
using Xunit;

public class CsvExporterTests
{
    [Fact]
    public void Converts_Result_Json_To_Csv()
    {
        var json = """{"columns":[{"name":"id"},{"name":"name"}],"rows":[["1","Ada"],["2","Linus,Torvalds"]],"rowCount":2}""";
        var csv = CsvExporter.ToCsv(json);
        Assert.Contains("id,name", csv);
        Assert.Contains("1,Ada", csv);
        Assert.Contains("\"Linus,Torvalds\"", csv);
    }
}

public class SavedAnalysisRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"sa_{Guid.NewGuid():N}.db");
    private readonly CatalogStore _store;

    public SavedAnalysisRepositoryTests()
    {
        _store = new CatalogStore(_db);
    }

    [Fact]
    public async Task Insert_List_Get_Delete_RoundTrip()
    {
        var repo = new SavedAnalysisRepository(_store);
        var id = await repo.InsertAsync(new SavedAnalysis(0, "My Analysis", "{\"x\":1}", System.DateTime.UtcNow));
        var all = await repo.ListAsync();
        Assert.Single(all);
        var got = await repo.GetAsync(id);
        Assert.Equal("My Analysis", got?.Name);

        await repo.DeleteAsync(id);
        Assert.Empty(await repo.ListAsync());
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
