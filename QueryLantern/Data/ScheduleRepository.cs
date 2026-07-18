namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// Persists scheduled/recurring analyses.
/// </summary>
public sealed class ScheduleRepository
{
    private readonly CatalogStore _catalog;

    public ScheduleRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> AddAsync(int connectionId, string question, string sql, string cadence)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Schedules (ConnectionId, Question, Sql, Cadence, LastRunAt, LastSummary)
            VALUES (@ConnectionId, @Question, @Sql, @Cadence, NULL, NULL);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        cmd.Parameters.Add(new SqliteParameter("@Question", question));
        cmd.Parameters.Add(new SqliteParameter("@Sql", sql));
        cmd.Parameters.Add(new SqliteParameter("@Cadence", cadence));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<ScheduledAnalysis>> ListAsync()
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Schedules ORDER BY Id;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<ScheduledAnalysis>();
        while (await reader.ReadAsync())
        {
            result.Add(ToSchedule(reader));
        }

        return result;
    }

    public async Task<ScheduledAnalysis?> GetAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Schedules WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ToSchedule(reader) : null;
    }

    public async Task UpdateRunAsync(int id, DateTime runAt, string summary)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Schedules SET LastRunAt = @RunAt, LastSummary = @Summary WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@RunAt", runAt.ToString("O")));
        cmd.Parameters.Add(new SqliteParameter("@Summary", summary));
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateRunWithResultAsync(int id, DateTime runAt, string summary, string resultJson)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Schedules SET LastRunAt = @RunAt, LastSummary = @Summary, LastResultJson = @Result WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@RunAt", runAt.ToString("O")));
        cmd.Parameters.Add(new SqliteParameter("@Summary", summary));
        cmd.Parameters.Add(new SqliteParameter("@Result", resultJson));
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Schedules WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static ScheduledAnalysis ToSchedule(SqliteDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("Id")),
        ConnectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
        Question: reader.GetString(reader.GetOrdinal("Question")),
        Sql: reader.GetString(reader.GetOrdinal("Sql")),
        Cadence: reader.GetString(reader.GetOrdinal("Cadence")),
        LastRunAt: reader.IsDBNull(reader.GetOrdinal("LastRunAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastRunAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        LastSummary: reader.IsDBNull(reader.GetOrdinal("LastSummary")) ? null : reader.GetString(reader.GetOrdinal("LastSummary")),
        LastResultJson: reader.IsDBNull(reader.GetOrdinal("LastResultJson")) ? null : reader.GetString(reader.GetOrdinal("LastResultJson")));
}
