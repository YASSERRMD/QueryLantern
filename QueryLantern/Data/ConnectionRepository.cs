namespace QueryLantern.Data;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// CRUD access for saved <see cref="ConnectionProfile"/> rows in the catalog.
/// </summary>
public sealed class ConnectionRepository
{
    private readonly CatalogStore _catalog;

    public ConnectionRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> InsertAsync(ConnectionProfile profile)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ConnectionProfiles
                (Name, Engine, Host, Port, DatabaseName, Username, SecretRef, Options, CreatedAt)
            VALUES
                (@Name, @Engine, @Host, @Port, @Database, @Username, @SecretRef, @Options, @CreatedAt);
            SELECT last_insert_rowid();
            """;
        AddParameters(cmd, profile);
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task<ConnectionProfile?> GetAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM ConnectionProfiles WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await ReadOneAsync(reader);
    }

    public async Task<IReadOnlyList<ConnectionProfile>> ListAsync()
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM ConnectionProfiles ORDER BY Name;";
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<ConnectionProfile>();
        while (await reader.ReadAsync())
        {
            result.Add(ToProfile(reader));
        }

        return result;
    }

    public async Task UpdateAsync(ConnectionProfile profile)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ConnectionProfiles SET
                Name = @Name, Engine = @Engine, Host = @Host, Port = @Port,
                DatabaseName = @Database, Username = @Username, SecretRef = @SecretRef,
                Options = @Options
            WHERE Id = @Id;
            """;
        cmd.Parameters.Add(new SqliteParameter("@Id", profile.Id));
        AddParameters(cmd, profile);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ConnectionProfiles WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand cmd, ConnectionProfile profile)
    {
        cmd.Parameters.Add(new SqliteParameter("@Name", profile.Name));
        cmd.Parameters.Add(new SqliteParameter("@Engine", (int)profile.Engine));
        cmd.Parameters.Add(new SqliteParameter("@Host", profile.Host));
        cmd.Parameters.Add(new SqliteParameter("@Port", profile.Port));
        cmd.Parameters.Add(new SqliteParameter("@Database", profile.Database));
        cmd.Parameters.Add(new SqliteParameter("@Username", profile.Username));
        cmd.Parameters.Add(new SqliteParameter("@SecretRef", profile.SecretRef));
        cmd.Parameters.Add(new SqliteParameter("@Options", profile.Options));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", profile.CreatedAt.ToString("O")));
    }

    private static async Task<ConnectionProfile?> ReadOneAsync(SqliteDataReader reader)
    {
        if (await reader.ReadAsync())
        {
            return ToProfile(reader);
        }

        return null;
    }

    private static ConnectionProfile ToProfile(SqliteDataReader r) => new()
    {
        Id = r.GetInt32("Id"),
        Name = r.GetString("Name"),
        Engine = (DatabaseEngine)r.GetInt32("Engine"),
        Host = r.GetString("Host"),
        Port = r.GetInt32("Port"),
        Database = r.GetString("DatabaseName"),
        Username = r.GetString("Username"),
        SecretRef = r.GetString("SecretRef"),
        Options = r.GetString("Options"),
        CreatedAt = DateTime.Parse(r.GetString("CreatedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
