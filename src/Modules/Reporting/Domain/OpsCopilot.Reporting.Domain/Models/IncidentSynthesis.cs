namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 89: deterministic cross-signal synthesis for a correlated incident view.
/// Built entirely from briefing signals; no LLM inference on render.
/// All string values are ASCII-safe (no em-dash or non-ASCII characters).
/// </summary>
public sealed record IncidentSynthesis(
    string  OverallAssessment,
    string? FailureMode,
    string? DataSignal,
    string? KnowledgeGap,
    string? ChangeCorrelation,
    string? SessionContext);
