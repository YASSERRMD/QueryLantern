namespace QueryLantern.Models;

/// <summary>
/// A saved, successful analysis stored in the query library for few-shot grounding of future questions.
/// Only the question, SQL and rating are stored, never result rows.
/// </summary>
public sealed record LibraryQuery(int Id, int ConnectionId, string Question, string Sql, int Rating, DateTime CreatedAt);
