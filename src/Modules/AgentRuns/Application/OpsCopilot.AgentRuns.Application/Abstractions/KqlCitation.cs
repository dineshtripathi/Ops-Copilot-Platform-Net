namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Evidence record attached to every triage response.
/// All fields come directly from the tool execution — no inference or enrichment.
/// </summary>
public sealed record KqlCitation(
    string         WorkspaceId,
    string         ExecutedQuery,
    string         Timespan,
    DateTimeOffset ExecutedAtUtc);
