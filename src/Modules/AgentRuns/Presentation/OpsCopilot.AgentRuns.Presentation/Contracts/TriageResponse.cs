using System.Text.Json;

namespace OpsCopilot.AgentRuns.Presentation.Contracts;

/// <summary>Serialised form of a KQL citation returned to callers.</summary>
/// <param name="WorkspaceId">Log Analytics workspace that was queried.</param>
/// <param name="ExecutedQuery">Exact KQL that was run.</param>
/// <param name="Timespan">ISO 8601 duration used as the query time range.</param>
/// <param name="ExecutedAtUtc">UTC timestamp when the query was executed.</param>
public sealed record CitationDto(
    string         WorkspaceId,
    string         ExecutedQuery,
    string         Timespan,
    DateTimeOffset ExecutedAtUtc);

/// <summary>Serialised form of a runbook citation returned to callers.</summary>
/// <param name="RunbookId">Unique identifier of the matched runbook.</param>
/// <param name="Title">Title of the runbook.</param>
/// <param name="Snippet">Relevant content snippet from the runbook.</param>
/// <param name="Score">Relevance score (0.0 – 1.0).</param>
public sealed record RunbookCitationDto(
    string RunbookId,
    string Title,
    string Snippet,
    double Score);

/// <summary>Response body for POST /agent/triage.</summary>
/// <param name="RunId">Unique identifier of the persisted AgentRun ledger entry.</param>
/// <param name="Status">Terminal status of the run (Completed / Degraded / Failed).</param>
/// <param name="Summary">Structured JSON summary (e.g. <c>{"rowCount":5}</c>). Null on failure.</param>
/// <param name="Citations">Evidence citations — one per KQL tool invocation.</param>
/// <param name="RunbookCitations">Runbook search citations — one per matched runbook.</param>
public sealed record TriageResponse(
    Guid                              RunId,
    string                            Status,
    JsonElement?                      Summary,
    IReadOnlyList<CitationDto>        Citations,
    IReadOnlyList<RunbookCitationDto> RunbookCitations,
    Guid?                             SessionId,
    bool                              IsNewSession,
    DateTimeOffset?                   SessionExpiresAtUtc,
    bool                              UsedSessionContext);
