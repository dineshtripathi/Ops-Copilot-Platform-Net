namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 87: Deterministic session-level triage briefing derived exclusively from
/// persisted RecentRunSummary fields (Status, AlertFingerprint, CreatedAtUtc, CompletedAtUtc).
/// Contains ONLY counts, patterns, and status flags — never raw LLM text or JSON blobs.
/// Null on SessionDetailResponse when the session has no runs.
/// </summary>
public sealed record SessionBriefing(
    int     RunCount,
    bool    IsIsolated,
    string  StatusPattern,
    string  FingerprintPattern,
    string? DominantFingerprint,
    string? SequenceConclusion);
