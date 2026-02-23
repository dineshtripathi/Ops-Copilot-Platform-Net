using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Enums;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>Value returned to the presentation layer after a triage run.</summary>
public sealed record TriageResult(
    Guid                              RunId,
    AgentRunStatus                    Status,
    string?                           SummaryJson,
    IReadOnlyList<KqlCitation>        Citations,
    IReadOnlyList<RunbookCitation>    RunbookCitations,
    Guid?                             SessionId,
    bool                              IsNewSession,
    DateTimeOffset?                   SessionExpiresAtUtc,
    bool                              UsedSessionContext);
