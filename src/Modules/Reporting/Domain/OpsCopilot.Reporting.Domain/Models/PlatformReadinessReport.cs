namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Lightweight composite readiness check that merges evaluation, connector, and action-type status.
/// </summary>
public sealed record PlatformReadinessReport(
    double EvaluationPassRate,
    int TotalConnectors,
    int TotalActionTypes,
    bool AllEvaluationsPassing,
    DateTime GeneratedAtUtc);
