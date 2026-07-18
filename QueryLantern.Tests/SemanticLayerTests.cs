namespace QueryLantern.Tests;

using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies the semantic layer stores business-term mappings per connection and surfaces them as a
/// glossary for the model.
/// </summary>
public class SemanticLayerTests
{
    [Fact]
    public void Term_Is_Stored_And_Listed_Per_Connection()
    {
        var repo = new SemanticLayerRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "revenue", SemanticTermKind.Metric, "SUM(amount)", "orders").GetAwaiter().GetResult();
        repo.AddAsync(2, "region", SemanticTermKind.Dimension, "country", null).GetAwaiter().GetResult();

        var conn1 = repo.ListAsync(1).GetAwaiter().GetResult();
        Assert.Single(conn1);
        Assert.Equal("SUM(amount)", conn1[0].Expression);
    }

    [Fact]
    public void Term_Can_Be_Deleted()
    {
        var repo = new SemanticLayerRepository(new CatalogStore(":memory:"));
        var id = repo.AddAsync(1, "revenue", SemanticTermKind.Metric, "SUM(amount)", null).GetAwaiter().GetResult();
        repo.DeleteAsync(id).GetAwaiter().GetResult();
        Assert.Empty(repo.ListAsync(1).GetAwaiter().GetResult());
    }

    [Fact]
    public void Glossary_Builds_Context_Block()
    {
        var repo = new SemanticLayerRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "revenue", SemanticTermKind.Metric, "SUM(amount)", "orders").GetAwaiter().GetResult();
        var service = new SemanticLayerService(repo);

        var glossary = service.BuildGlossaryAsync(1).GetAwaiter().GetResult();
        Assert.Contains("revenue", glossary);
        Assert.Contains("SUM(amount)", glossary);

        Assert.Equal(string.Empty, service.BuildGlossaryAsync(99).GetAwaiter().GetResult());
    }

    [Fact]
    public void Empty_Term_Or_Expression_Is_Rejected()
    {
        var repo = new SemanticLayerRepository(new CatalogStore(":memory:"));
        var service = new SemanticLayerService(repo);
        Assert.Equal(0, service.AddTermAsync(1, "  ", SemanticTermKind.Metric, "x", null).GetAwaiter().GetResult());
        Assert.Equal(0, service.AddTermAsync(1, "term", SemanticTermKind.Metric, "  ", null).GetAwaiter().GetResult());
    }
}
