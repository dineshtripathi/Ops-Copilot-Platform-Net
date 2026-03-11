using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Enums;

namespace OpsCopilot.AgentRuns.Application.Orchestration;

/// <summary>Value returned to the presentation layer after a triage run.</summary>
public sealed record TriageResult(
    Guid                              RunId,
    AgentRunStatus                    Status,
    string?                           SummaryJson,
    IReadOnlyList<KqlCitation>        Citations,
    IReadOnlyList<RunbookCitation>         RunbookCitations,
    IReadOnlyList<MemoryCitation>          MemoryCitations,
    IReadOnlyList<DeploymentDiffCitation>  DeploymentDiffCitations,
    Guid?                                  SessionId,
    bool                              IsNewSession,
    DateTimeOffset?                   SessionExpiresAtUtc,
    bool                              UsedSessionContext,
    string                            SessionReasonCode,
    string?  ModelId         = null,
    string?  PromptVersionId = null,
    int?     InputTokens     = null,
    int?     OutputTokens    = null,
    int?     TotalTokens     = null,
    decimal? EstimatedCost   = null);
