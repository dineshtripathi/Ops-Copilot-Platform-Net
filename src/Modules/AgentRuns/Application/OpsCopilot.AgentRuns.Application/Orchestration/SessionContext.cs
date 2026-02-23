using OpsCopilot.AgentRuns.Domain.Enums;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>
/// Compact summary of a prior triage run within the same session.
/// Intentionally tiny — no LLM payloads, no full JSON, just IDs + statuses.
/// </summary>
public sealed record PriorRunSummary(
    Guid           RunId,
    AgentRunStatus Status,
    string?        AlertFingerprint,
    int            CitationCount,
    int            RunbookCitationCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Lightweight session context loaded when an existing session is resumed.
/// Contains the last N run summaries — deterministic and audit-friendly.
/// </summary>
public sealed record SessionContext(
    IReadOnlyList<PriorRunSummary> PriorRuns);
