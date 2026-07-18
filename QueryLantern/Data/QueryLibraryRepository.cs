namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// Persists successful analyses in a per-connection query library for few-shot grounding.
/// </summary>
public sealed class QueryLibraryRepository
{
    private readonly CatalogStore _catalog;

    public QueryLibraryRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> AddAsync(int connectionId, string question, string sql, int rating = 0)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO QueryLibrary (ConnectionId, Question, Sql, Rating, CreatedAt)
            VALUES (@ConnectionId, @Question, @Sql, @Rating, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        cmd.Parameters.Add(new SqliteParameter("@Question", question));
        cmd.Parameters.Add(new SqliteParameter("@Sql", sql));
        cmd.Parameters.Add(new SqliteParameter("@Rating", rating));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", DateTime.UtcNow.ToString("O")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<LibraryQuery>> ListAsync(int connectionId)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM QueryLibrary WHERE ConnectionId = @ConnectionId ORDER BY Rating DESC, CreatedAt DESC;";
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<LibraryQuery>();
        while (await reader.ReadAsync())
        {
            result.Add(ToQuery(reader));
        }

        return result;
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM QueryLibrary WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static LibraryQuery ToQuery(SqliteDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("Id")),
        ConnectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
        Question: reader.GetString(reader.GetOrdinal("Question")),
        Sql: reader.GetString(reader.GetOrdinal("Sql")),
        Rating: reader.GetInt32(reader.GetOrdinal("Rating")),
        CreatedAt: DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
