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

/// <summary>Response body for POST /agent/triage.</summary>
/// <param name="RunId">Unique identifier of the persisted AgentRun ledger entry.</param>
/// <param name="Status">Terminal status of the run (Completed / Degraded / Failed).</param>
/// <param name="Summary">LLM-generated or structured summary (null on failure).</param>
/// <param name="Citations">Evidence citations — one per KQL tool invocation.</param>
public sealed record TriageResponse(
    Guid                      RunId,
    string                    Status,
    string?                   Summary,
    IReadOnlyList<CitationDto> Citations);
