namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 86: Structural triage briefing derived exclusively from persisted truth.
/// Contains ONLY counts, rates, and status flags — never raw LLM text or JSON blobs.
/// Null on RunDetailResponse when the run is non-terminal or briefing data is unavailable.
/// </summary>
public sealed record RunBriefing(
    string   StatusSeverity,
    double?  DurationSeconds,
    double?  ToolSuccessRate,
    int      KqlRowCount,
    int      RunbookHitCount,
    int      MemoryHitCount,
    int      DeploymentDiffHitCount,
    int      KqlCitationCount,
    bool     HasRecommendedActions,
    string?  FailureSignal);
