namespace QueryLantern.Models;

/// <summary>
/// Supported database engines. Each maps to an <c>IDatabaseAdapter</c> implementation.
/// </summary>
public enum DatabaseEngine
{
    Sqlite,
    Postgresql,
    MySql,
    SqlServer,
    Oracle,
    DuckDb,
    ClickHouse,
    Odbc
}

/// <summary>
/// A saved database connection profile. Secret material is stored only as a reference that the
/// secret vault resolves on demand, never as plaintext in the catalog.
/// </summary>
public sealed record ConnectionProfile
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DatabaseEngine Engine { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Database { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string SecretRef { get; init; } = string.Empty;
    public string Options { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
