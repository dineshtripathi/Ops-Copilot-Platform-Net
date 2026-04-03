namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Safe, read-only summary of live observability evidence for a run.
/// Contains only curated summaries derived from governed pack execution.
/// </summary>
public sealed record ObservabilityEvidenceSummary(
    string Source,
    int CollectorCount,
    int SuccessfulCollectors,
    int FailedCollectors,
    IReadOnlyList<ObservabilityEvidenceCollectorSummary> CollectorSummaries,
    string? Diagnostic = null,
    string CoverageStatus = "unknown",
    bool IsActionable = false,
    IReadOnlyList<string>? Recommendations = null,
    string? FailurePattern = null,
    string? OwnerPath = null);

/// <summary>
/// Safe summary of a single observability evidence collector result.
/// No raw query text or payload bodies are exposed.
/// </summary>
public sealed record ObservabilityEvidenceCollectorSummary(
    string CollectorId,
    string Title,
    int RowCount,
    string Status,
    IReadOnlyList<string> Highlights,
    string? ErrorMessage = null,
    string? RunbookRef = null);