namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Per-tool aggregated call statistics within an optional date range/tenant scope.
/// </summary>
public sealed record ToolUsageSummaryRow(
    string ToolName,
    int TotalCalls,
    int SuccessfulCalls,
    int FailedCalls,
    double AvgDurationMs);
