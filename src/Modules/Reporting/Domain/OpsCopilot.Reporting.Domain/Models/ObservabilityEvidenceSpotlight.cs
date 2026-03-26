namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Safe observability spotlight anchored to a single run.
/// Reuses governed App Insights evidence summaries without exposing raw query text or payloads.
/// </summary>
public sealed record ObservabilityEvidenceSpotlight(
    Guid RunId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    ObservabilityEvidenceSummary Evidence);