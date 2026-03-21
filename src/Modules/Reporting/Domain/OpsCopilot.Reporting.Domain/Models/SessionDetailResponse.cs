namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 67: Safe read-only projection of a session with its tenant-scoped runs.
/// Safe fields only — no raw evidence, no CitationsJson, no prompts.
/// </summary>
public sealed record SessionDetailResponse(
    Guid SessionId,
    IReadOnlyList<RecentRunSummary> Runs,
    SessionBriefing? Briefing = null,
    // Slice 88: deterministic next-step hints — no LLM, no raw data
    IReadOnlyList<RunRecommendation>? SessionRecommendations = null,
    // Slice 89: deterministic correlated incident view — no LLM, no raw data
    IncidentSynthesis? SessionSynthesis = null);
