namespace QueryLantern.Adapters;

using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Models;

/// <summary>
/// The common contract every database engine implements. The UI and the agent tools work against
/// this interface so no engine specific code leaks into the product surface.
/// </summary>
public interface IDatabaseAdapter : IDisposable
{
    /// <summary>The engine this adapter targets.</summary>
    DatabaseEngine Engine { get; }

    /// <summary>Opens and validates a connection using the supplied profile and password.</summary>
    Task OpenAsync(ConnectionProfile profile, string? password, CancellationToken ct = default);

    /// <summary>Closes the connection if open.</summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>Runs a lightweight connectivity check and returns success plus a human message.</summary>
    Task<TestResult> TestConnectionAsync(ConnectionProfile profile, string? password, CancellationToken ct = default);

    /// <summary>Executes a read only query and returns a capped result set.</summary>
    Task<QueryResult> ExecuteReadAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, int maxRows = 1000, CancellationToken ct = default);

    /// <summary>Introspects the connected database and returns a normalized schema model.</summary>
    Task<SchemaModel> IntrospectSchemaAsync(CancellationToken ct = default);

    /// <summary>Executes a mutating statement. Only called after explicit human approval.</summary>
    Task<int> ExecuteWriteAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default);
}
