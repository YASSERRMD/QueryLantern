namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// CRUD access for saved analyses: a named snapshot of a chat (messages, connection/provider ids,
/// and any result set) so a conversation can be reopened later.
/// </summary>
public sealed class SavedAnalysisRepository
{
    private readonly CatalogStore _catalog;

    public SavedAnalysisRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> InsertAsync(SavedAnalysis analysis)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SavedAnalyses (Name, Payload, CreatedAt)
            VALUES (@Name, @Payload, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@Name", analysis.Name));
        cmd.Parameters.Add(new SqliteParameter("@Payload", analysis.Payload));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", analysis.CreatedAt.ToString("O")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<SavedAnalysis>> ListAsync()
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SavedAnalyses ORDER BY CreatedAt DESC;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<SavedAnalysis>();
        while (await reader.ReadAsync())
        {
            result.Add(ToAnalysis(reader));
        }
        return result;
    }

    public async Task<SavedAnalysis?> GetAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SavedAnalyses WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ToAnalysis(reader) : null;
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SavedAnalyses WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static SavedAnalysis ToAnalysis(SqliteDataReader reader) => new(
        Id: reader.GetInt32("Id"),
        Name: reader.GetString("Name"),
        Payload: reader.GetString("Payload"),
        CreatedAt: DateTime.Parse(reader.GetString("CreatedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind));
}

/// <summary>
/// A saved analysis snapshot. The payload is opaque JSON produced by the chat surface.
/// </summary>
public sealed record SavedAnalysis(int Id, string Name, string Payload, DateTime CreatedAt);
