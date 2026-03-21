namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Composite dashboard overview: agent-run summary, trend line, top-tool breakdown,
/// and recent run list. All fields are derived from existing read models — no fabricated metrics.
/// </summary>
public sealed record DashboardOverviewResponse(
    AgentRunsSummaryReport              Summary,
    IReadOnlyList<AgentRunsTrendPoint>   Trend,
    IReadOnlyList<ToolUsageSummaryRow>   TopTools,
    IReadOnlyList<RecentRunSummary>      RecentRuns);
