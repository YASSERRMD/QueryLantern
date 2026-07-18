namespace QueryLantern.Data;

using System.Data;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// A lightweight, EF free SQLite catalog that stores saved connection and provider profiles.
/// Secret material is stored only as a reference string; the vault resolves it on demand.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public CatalogStore(string catalogPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = catalogPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        Bootstrap();
    }

    /// <summary>
    /// Creates the catalog tables if they do not exist.
    /// </summary>
    public void Bootstrap()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ConnectionProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Engine INTEGER NOT NULL,
                Host TEXT NOT NULL,
                Port INTEGER NOT NULL,
                DatabaseName TEXT NOT NULL,
                Username TEXT NOT NULL,
                SecretRef TEXT NOT NULL,
                Options TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProviderProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                BaseUrl TEXT NOT NULL,
                ModelId TEXT NOT NULL,
                KeyRef TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SavedAnalyses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Payload TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Plans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId INTEGER NOT NULL,
                Question TEXT NOT NULL,
                Payload TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SchemaMemory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId INTEGER NOT NULL,
                Fact TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SemanticTerms (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId INTEGER NOT NULL,
                Term TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                Expression TEXT NOT NULL,
                TableName TEXT
            );

            CREATE TABLE IF NOT EXISTS QueryLibrary (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConnectionId INTEGER NOT NULL,
                Question TEXT NOT NULL,
                Sql TEXT NOT NULL,
                Rating INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    internal SqliteConnection Connection => _connection;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }
}
