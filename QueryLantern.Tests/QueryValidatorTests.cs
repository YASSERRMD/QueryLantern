namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Services;
using QueryLantern.Tools;
using Xunit;

/// <summary>
/// Verifies SQL dry-run validation catches invalid queries before execution and that the governed
/// run_query entry point never executes a rejected query.
/// </summary>
public class QueryValidatorTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_validate_{Guid.NewGuid():N}.db");
    private readonly SqliteAdapter _adapter;

    public QueryValidatorTests()
    {
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE sales (region TEXT, amount INTEGER);").GetAwaiter().GetResult();
    }

    [Fact]
    public void Valid_Select_Passes_DryRun()
    {
        var validator = new QueryValidator(_adapter);
        var result = validator.Validate("SELECT region, SUM(amount) FROM sales GROUP BY region;");
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Invalid_Column_Is_Caught_At_DryRun()
    {
        var validator = new QueryValidator(_adapter);
        var result = validator.Validate("SELECT nonexistent_column FROM sales;");
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void RunQueryValidated_Does_Not_Execute_Rejected_Query()
    {
        var query = new QueryTools(_adapter);
        // A bad column name must be rejected at dry-run, not executed.
        var feedback = query.RunQueryValidated("SELECT missing_col FROM sales;");
        Assert.Contains("\"valid\":false", feedback);
        Assert.DoesNotContain("\"rowCount\"", feedback);

        // The table must be unchanged (no execution happened for the rejected statement).
        var after = _adapter.ExecuteReadAsync("SELECT COUNT(*) AS c FROM sales;").GetAwaiter().GetResult();
        Assert.Equal(0L, after.Rows[0][0]);
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
