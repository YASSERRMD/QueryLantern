namespace QueryLantern.Adapters;

using QueryLantern.Models;

/// <summary>
/// Builds the correct <see cref="IDatabaseAdapter"/> for a given engine. Engines are added here as
/// their adapters land in later phases (PostgreSQL, MySQL, SQL Server, Oracle, DuckDB, ClickHouse,
/// ODBC). SQLite is available from Phase 4.
/// </summary>
public static class AdapterFactory
{
    public static IDatabaseAdapter Create(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Sqlite => new SqliteAdapter(),
        DatabaseEngine.Postgresql => new PostgresAdapter(),
        DatabaseEngine.MySql => new MySqlAdapter(),
        DatabaseEngine.SqlServer => new SqlServerAdapter(),
        DatabaseEngine.Oracle => new OracleAdapter(),
        DatabaseEngine.DuckDb => new DuckDbAdapter(),
        DatabaseEngine.ClickHouse => new ClickHouseAdapter(),
        DatabaseEngine.Odbc => new OdbcAdapter(),
        _ => throw new NotSupportedException($"Engine {engine} is not supported yet.")
    };
}
