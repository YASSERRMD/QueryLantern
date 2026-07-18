namespace QueryLantern.Tests;

using QueryLantern.Data;
using QueryLantern.Models;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies the query library stores successful analyses per connection, ranks similar questions by
/// shared keywords, and builds few-shot examples for the model.
/// </summary>
public class QueryLibraryTests
{
    [Fact]
    public void Query_Is_Saved_And_Listed_Per_Connection()
    {
        var repo = new QueryLibraryRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "total revenue by region", "SELECT region, SUM(amount) FROM orders GROUP BY region", 1).GetAwaiter().GetResult();
        repo.AddAsync(2, "unrelated", "SELECT 1", 0).GetAwaiter().GetResult();

        var conn1 = repo.ListAsync(1).GetAwaiter().GetResult();
        Assert.Single(conn1);
        Assert.Contains("SUM(amount)", conn1[0].Sql);
    }

    [Fact]
    public void Similar_Questions_Are_Ranked_By_Shared_Keywords()
    {
        var repo = new QueryLibraryRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "total revenue by region", "SQL_A", 1).GetAwaiter().GetResult();
        repo.AddAsync(1, "count orders by country", "SQL_B", 1).GetAwaiter().GetResult();
        repo.AddAsync(1, "delete everything", "SQL_C", 0).GetAwaiter().GetResult();
        var service = new QueryLibraryService(repo);

        var similar = service.FindSimilarAsync(1, "revenue per region last quarter").GetAwaiter().GetResult();
        Assert.NotEmpty(similar);
        Assert.Equal("SQL_A", similar[0].Sql);
    }

    [Fact]
    public void FewShot_Block_Built_From_Similar_Queries()
    {
        var repo = new QueryLibraryRepository(new CatalogStore(":memory:"));
        repo.AddAsync(1, "total revenue by region", "SQL_A", 1).GetAwaiter().GetResult();
        var service = new QueryLibraryService(repo);

        var block = service.BuildFewShotAsync(1, "revenue by region").GetAwaiter().GetResult();
        Assert.Contains("SQL_A", block);

        Assert.Equal(string.Empty, service.BuildFewShotAsync(99, "anything").GetAwaiter().GetResult());
    }

    [Fact]
    public void Empty_Question_Or_Sql_Is_Rejected()
    {
        var repo = new QueryLibraryRepository(new CatalogStore(":memory:"));
        var service = new QueryLibraryService(repo);
        Assert.Equal(0, service.SaveAsync(1, "  ", "SELECT 1", 0).GetAwaiter().GetResult());
        Assert.Equal(0, service.SaveAsync(1, "q", "   ", 0).GetAwaiter().GetResult());
    }
}
