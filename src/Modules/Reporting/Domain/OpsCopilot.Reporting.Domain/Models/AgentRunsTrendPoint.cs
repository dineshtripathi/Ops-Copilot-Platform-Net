namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// One calendar-day data point in the agent-runs time-series trend.
/// </summary>
public sealed record AgentRunsTrendPoint(
    DateOnly DateUtc,
    int TotalRuns,
    int CompletedRuns,
    int FailedRuns,
    int DegradedRuns);
