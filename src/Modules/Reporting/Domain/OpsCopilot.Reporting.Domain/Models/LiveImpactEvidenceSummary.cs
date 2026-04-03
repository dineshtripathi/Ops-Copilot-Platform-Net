namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Live tenant-scoped impact evidence derived from governed workspace queries.
/// Keeps blast-radius and policy activity signals available even when DB projections are stale.
/// </summary>
public sealed record LiveImpactEvidenceSummary(
    string Source,
    BlastRadiusSummary? BlastRadius,
    ActivitySignalSummary? ActivitySignals,
    string? Diagnostic = null,
    string CoverageStatus = "unknown",
    bool IsActionable = false,
    int SuccessfulCollectors = 0,
    int FailedCollectors = 0,
    IReadOnlyList<string>? Recommendations = null);
