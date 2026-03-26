namespace OpsCopilot.Reporting.Domain.Models;

public sealed record DashboardDataFreshness(
    DateTimeOffset LiveEvaluatedAtUtc,
    DateTimeOffset? LatestHistoricalRunAtUtc,
    bool HistoricalDataIsStale);
