namespace OpsCopilot.Reporting.Domain.Models;

public sealed record MttrTrendPoint(
    DateOnly BucketDate,
    string?  IncidentCategory,
    double   AvgResolutionMinutes,
    int      Count);
