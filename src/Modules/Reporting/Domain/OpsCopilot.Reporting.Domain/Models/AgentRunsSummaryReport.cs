namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Aggregate metrics for agent runs within an optional date range and optional tenant scope.
/// Nullable aggregate fields are null when no qualifying data exists.
/// </summary>
public sealed record AgentRunsSummaryReport(
    int TotalRuns,
    int Completed,
    int Failed,
    int Degraded,
    int Pending,
    int Running,
    double? AvgDurationMs,
    double? AvgTotalTokens,
    decimal? TotalEstimatedCost,
    double CitationCoverageRate,
    DateTime? FromUtc,
    DateTime? ToUtc);
