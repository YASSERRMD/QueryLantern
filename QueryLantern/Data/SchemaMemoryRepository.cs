namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// Persists learned schema facts per connection so the agent accumulates durable knowledge about a
/// database's quirks, synonyms and conventions across sessions.
/// </summary>
public sealed class SchemaMemoryRepository
{
    private readonly CatalogStore _catalog;

    public SchemaMemoryRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> AddAsync(int connectionId, string fact, SchemaFactKind kind)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SchemaMemory (ConnectionId, Fact, Kind, CreatedAt)
            VALUES (@ConnectionId, @Fact, @Kind, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        cmd.Parameters.Add(new SqliteParameter("@Fact", fact));
        cmd.Parameters.Add(new SqliteParameter("@Kind", (int)kind));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", DateTime.UtcNow.ToString("O")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<SchemaFact>> ListAsync(int connectionId)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SchemaMemory WHERE ConnectionId = @ConnectionId ORDER BY CreatedAt DESC;";
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<SchemaFact>();
        while (await reader.ReadAsync())
        {
            result.Add(ToFact(reader));
        }

        return result;
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SchemaMemory WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static SchemaFact ToFact(SqliteDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("Id")),
        ConnectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
        Fact: reader.GetString(reader.GetOrdinal("Fact")),
        Kind: (SchemaFactKind)reader.GetInt32(reader.GetOrdinal("Kind")),
        CreatedAt: DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
