namespace QueryLantern.Data;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QueryLantern.Models;

/// <summary>
/// Persists the semantic layer: business terms mapped to schema columns or expressions per connection.
/// </summary>
public sealed class SemanticLayerRepository
{
    private readonly CatalogStore _catalog;

    public SemanticLayerRepository(CatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task<int> AddAsync(int connectionId, string term, SemanticTermKind kind, string expression, string? table)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SemanticTerms (ConnectionId, Term, Kind, Expression, TableName)
            VALUES (@ConnectionId, @Term, @Kind, @Expression, @Table);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        cmd.Parameters.Add(new SqliteParameter("@Term", term));
        cmd.Parameters.Add(new SqliteParameter("@Kind", (int)kind));
        cmd.Parameters.Add(new SqliteParameter("@Expression", expression));
        cmd.Parameters.Add(new SqliteParameter("@Table", (object?)table ?? System.DBNull.Value));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<SemanticTerm>> ListAsync(int connectionId)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SemanticTerms WHERE ConnectionId = @ConnectionId ORDER BY Term;";
        cmd.Parameters.Add(new SqliteParameter("@ConnectionId", connectionId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<SemanticTerm>();
        while (await reader.ReadAsync())
        {
            result.Add(ToTerm(reader));
        }

        return result;
    }

    public async Task DeleteAsync(int id)
    {
        await using var cmd = _catalog.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SemanticTerms WHERE Id = @Id;";
        cmd.Parameters.Add(new SqliteParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync();
    }

    private static SemanticTerm ToTerm(SqliteDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("Id")),
        ConnectionId: reader.GetInt32(reader.GetOrdinal("ConnectionId")),
        Term: reader.GetString(reader.GetOrdinal("Term")),
        Kind: (SemanticTermKind)reader.GetInt32(reader.GetOrdinal("Kind")),
        Expression: reader.GetString(reader.GetOrdinal("Expression")),
        Table: reader.IsDBNull(reader.GetOrdinal("TableName")) ? null : reader.GetString(reader.GetOrdinal("TableName")));
}
