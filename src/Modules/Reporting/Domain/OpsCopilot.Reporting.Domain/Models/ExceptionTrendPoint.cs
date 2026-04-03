namespace OpsCopilot.Reporting.Domain.Models;

public sealed record ExceptionTrendPoint(
    DateOnly DateUtc,
    int ExceptionSignals,
    int FailedRuns,
    int DegradedRuns);
