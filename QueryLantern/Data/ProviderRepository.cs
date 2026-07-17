namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// CRUD access for saved <see cref="ProviderProfile"/> rows in the catalog.
/// </summary>
public sealed class ProviderRepository
{
    private readonly CatalogStore _catalog;

    public ProviderRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> InsertAsync(ProviderProfile profile)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ProviderProfiles (Name, Kind, BaseUrl, ModelId, KeyRef, CreatedAt)
            VALUES (@Name, @Kind, @BaseUrl, @ModelId, @KeyRef, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        AddParameters(cmd, profile);
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task<ProviderProfile?> GetAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProviderProfiles WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await ReadOneAsync(reader);
    }

    public async Task<IReadOnlyList<ProviderProfile>> ListAsync()
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM ProviderProfiles ORDER BY Name;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<ProviderProfile>();
        while (await reader.ReadAsync())
        {
            result.Add(ToProfile(reader));
        }

        return result;
    }

    public async Task UpdateAsync(ProviderProfile profile)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ProviderProfiles SET
                Name = @Name, Kind = @Kind, BaseUrl = @BaseUrl,
                ModelId = @ModelId, KeyRef = @KeyRef
            WHERE Id = @Id;
            """;
        cmd.Parameters.Add(new SqliteParameter("@Id", profile.Id));
        AddParameters(cmd, profile);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ProviderProfiles WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand cmd, ProviderProfile profile)
    {
        cmd.Parameters.Add(new SqliteParameter("@Name", profile.Name));
        cmd.Parameters.Add(new SqliteParameter("@Kind", (int)profile.Kind));
        cmd.Parameters.Add(new SqliteParameter("@BaseUrl", profile.BaseUrl));
        cmd.Parameters.Add(new SqliteParameter("@ModelId", profile.ModelId));
        cmd.Parameters.Add(new SqliteParameter("@KeyRef", profile.KeyRef));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", profile.CreatedAt.ToString("O")));
    }

    private static async Task<ProviderProfile?> ReadOneAsync(SqliteDataReader reader)
    {
        if (await reader.ReadAsync())
        {
            return ToProfile(reader);
        }

        return null;
    }

    private static ProviderProfile ToProfile(SqliteDataReader r) => new()
    {
        Id = r.GetInt32("Id"),
        Name = r.GetString("Name"),
        Kind = (ProviderKind)r.GetInt32("Kind"),
        BaseUrl = r.GetString("BaseUrl"),
        ModelId = r.GetString("ModelId"),
        KeyRef = r.GetString("KeyRef"),
        CreatedAt = DateTime.Parse(r.GetString("CreatedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
