namespace QueryLantern.Tests;

using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies learned schema facts persist per connection, can be listed and deleted, and are surfaced
/// as grounding context for the model.
/// </summary>
public class SchemaMemoryTests
{
    [Fact]
    public void Facts_Are_Stored_Per_Connection_And_Listed()
    {
        var repo = new SchemaMemoryRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "orders.status uses 'C' for cancelled", SchemaFactKind.Convention).GetAwaiter().GetResult();
        repo.AddAsync(2, "unrelated connection fact", SchemaFactKind.Quirk).GetAwaiter().GetResult();

        var conn1 = repo.ListAsync(1).GetAwaiter().GetResult();
        Assert.Single(conn1);
        Assert.Equal("orders.status uses 'C' for cancelled", conn1[0].Fact);
    }

    [Fact]
    public void Fact_Can_Be_Deleted()
    {
        var repo = new SchemaMemoryRepository(new CatalogStore(":memory:"));
        var id = repo.AddAsync(1, "a quirk", SchemaFactKind.Quirk).GetAwaiter().GetResult();
        repo.DeleteAsync(id).GetAwaiter().GetResult();
        Assert.Empty(repo.ListAsync(1).GetAwaiter().GetResult());
    }

    [Fact]
    public void Summarize_Builds_Prompt_Context()
    {
        var repo = new SchemaMemoryRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "use UTC timestamps", SchemaFactKind.Convention).GetAwaiter().GetResult();
        var service = new SchemaMemoryService(repo);

        var prompt = service.SummarizeForPromptAsync(1).GetAwaiter().GetResult();
        Assert.Contains("use UTC timestamps", prompt);

        var empty = service.SummarizeForPromptAsync(99).GetAwaiter().GetResult();
        Assert.Equal(string.Empty, empty);
    }

    [Fact]
    public void Empty_Fact_Is_Not_Recorded()
    {
        var repo = new SchemaMemoryRepository(new CatalogStore(":memory:"));
        var service = new SchemaMemoryService(repo);
        var id = service.RecordAsync(1, "   ").GetAwaiter().GetResult();
        Assert.Equal(0, id);
        Assert.Empty(repo.ListAsync(1).GetAwaiter().GetResult());
    }
}
