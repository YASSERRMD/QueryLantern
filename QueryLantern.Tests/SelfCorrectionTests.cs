namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Adapters;
using QueryLantern.Models;
using QueryLantern.Schema;
using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies error-driven self-correction: a query with a wrong column is repaired using the schema and
/// succeeds within the retry budget, and the budget is enforced.
/// </summary>
public class SelfCorrectionTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"ql_repair_{Guid.NewGuid():N}.db");
    private readonly SqliteAdapter _adapter;

    public SelfCorrectionTests()
    {
        _adapter = new SqliteAdapter();
        _adapter.OpenAsync(new ConnectionProfile { Engine = DatabaseEngine.Sqlite, Database = _db }, null).GetAwaiter().GetResult();
        _adapter.ExecuteWriteAsync("CREATE TABLE orders (id INTEGER, customer_id INTEGER);").GetAwaiter().GetResult();
    }

    private SchemaModel Schema() => _adapter.IntrospectSchemaAsync().GetAwaiter().GetResult();

    [Fact]
    public void Wrong_Column_Is_Repaired_And_Succeeds_Within_Budget()
    {
        // Misspelled column "cust_id" should be repaired to "customer_id".
        var sql = "SELECT id FROM orders WHERE cust_id = 1;";
        var validator = new QueryValidator(_adapter);
        var repairer = new QueryRepairer(Schema());
        var service = new SelfCorrectionService(validator, repairer);

        var outcome = service.Correct(sql);

        Assert.True(outcome.Succeeded);
        Assert.Contains("customer_id", outcome.FinalSql);
        Assert.True(outcome.Attempts.Count <= 3);
        // The corrected query must now validate.
        Assert.True(validator.Validate(outcome.FinalSql).IsValid);
    }

    [Fact]
    public void Budget_Is_Enforced_When_Unrepairable()
    {
        // A query that cannot be repaired (table truly missing and no fuzzy match) exhausts the budget.
        var sql = "SELECT * FROM no_such_table_xyz;";
        var validator = new QueryValidator(_adapter);
        var repairer = new QueryRepairer(Schema());
        var service = new SelfCorrectionService(validator, repairer);

        var outcome = service.Correct(sql, maxAttempts: 3);

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.BudgetExhausted);
        // Original failed attempt plus the three allowed corrections.
        Assert.Equal(4, outcome.Attempts.Count);
    }

    public void Dispose()
    {
        _adapter.CloseAsync().GetAwaiter().GetResult();
        _adapter.Dispose();
        if (File.Exists(_db)) File.Delete(_db);
    }
}
