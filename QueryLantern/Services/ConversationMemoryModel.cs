namespace QueryLantern.Services;

using System.Collections.Generic;

/// <summary>
/// A single resolved turn in a conversation, used to resolve follow-up questions.
/// </summary>
public sealed record ConversationTurn(string Question, string Sql, string ResultSummary);

/// <summary>
/// Outcome of resolving a (possibly elliptical) follow-up question against prior turns.
/// </summary>
public sealed record ResolvedQuestion(string Original, string Resolved, bool WasFollowUp, int? AnchorTurnIndex);
