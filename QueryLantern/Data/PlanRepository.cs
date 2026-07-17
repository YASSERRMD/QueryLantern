namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// Persists analyst plans against the conversation so every generated plan is auditable and can be
/// reloaded. The payload is the serialized PlanGraph JSON; the question is stored separately for
/// quick listing. No row data or secrets are ever stored here.
/// </summary>
public sealed class PlanRepository
{
    private readonly CatalogStore _catalog;

    public PlanRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> InsertAsync(StoredPlan plan)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Plans (ConnectionId, Question, Payload, CreatedAt)
            VALUES (@ConnectionId, @Question, @Payload, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", plan.ConnectionId));
        cmd.Parameters.Add(new SqliteParameter("@Question", plan.Question));
        cmd.Parameters.Add(new SqliteParameter("@Payload", plan.Payload));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", plan.CreatedAt.ToString("O")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<StoredPlan>> ListByConnectionAsync(int connectionId)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Plans WHERE ConnectionId = @ConnectionId ORDER BY CreatedAt DESC;";
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<StoredPlan>();
        while (await reader.ReadAsync())
        {
            result.Add(ToPlan(reader));
        }
        return result;
    }

    public async Task<StoredPlan?> GetAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Plans WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ToPlan(reader) : null;
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Plans WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static StoredPlan ToPlan(SqliteDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("Id")),
        ConnectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
        Question: reader.GetString(reader.GetOrdinal("Question")),
        Payload: reader.GetString(reader.GetOrdinal("Payload")),
        CreatedAt: DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind));
}

/// <summary>
/// A persisted plan. Payload is the serialized PlanGraph; only structural and semantic facts are stored,
/// never row data or secrets.
/// </summary>
public sealed record StoredPlan(int Id, int ConnectionId, string Question, string Payload, DateTime CreatedAt);
