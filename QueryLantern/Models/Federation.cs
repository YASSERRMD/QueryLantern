namespace QueryLantern.Models;

/// <summary>
/// Description of one side of a cross-connection join.
/// </summary>
public sealed record FederationSide(int ConnectionId, string Table, string KeyColumn);

/// <summary>
/// A cross-connection join request federation across two databases.
/// </summary>
public sealed record FederationRequest(
    FederationSide Left,
    FederationSide Right,
    string? SelectColumns = null);
