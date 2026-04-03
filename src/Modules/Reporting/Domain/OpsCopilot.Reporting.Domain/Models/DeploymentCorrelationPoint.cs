namespace OpsCopilot.Reporting.Domain.Models;

public sealed record DeploymentCorrelationPoint(
    DateOnly DateUtc,
    int RunsWithDeploymentChanges,
    int FailedOrDegradedWithChanges,
    double FailureRate);
