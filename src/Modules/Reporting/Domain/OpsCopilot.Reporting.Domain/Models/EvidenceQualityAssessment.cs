namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Slice 98: Bounded, deterministic evidence-quality assessment.
/// Derived entirely from already-computed safe evidence signals — no live LLM call,
/// no external I/O, no mutations, no raw payloads.
/// </summary>
public sealed record EvidenceQualityAssessment(
    EvidenceStrength Strength,
    EvidenceCompleteness Completeness,
    int SignalFamiliesPresent,
    int SignalFamiliesTotal,
    IReadOnlyList<string> MissingAreas,
    string Guidance,
    DateTimeOffset EvaluatedAt);

/// <summary>
/// Categorical coverage of distinct signal families actually present for the run.
/// </summary>
public enum EvidenceStrength
{
    /// <summary>5 or more distinct signal families present.</summary>
    Strong,
    /// <summary>3–4 distinct signal families present.</summary>
    Moderate,
    /// <summary>1–2 distinct signal families present.</summary>
    Weak,
    /// <summary>No signal families present (run produced no analysable evidence).</summary>
    Insufficient
}

/// <summary>
/// Categorical completeness relative to the maximum possible signal families.
/// </summary>
public enum EvidenceCompleteness
{
    /// <summary>All 8 signal families present.</summary>
    Complete,
    /// <summary>Some signal families present but not all.</summary>
    Partial,
    /// <summary>Fewer than 3 signal families present.</summary>
    Sparse
}
