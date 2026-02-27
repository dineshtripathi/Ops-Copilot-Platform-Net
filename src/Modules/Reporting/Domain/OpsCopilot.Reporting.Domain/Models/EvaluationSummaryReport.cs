namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Read-only snapshot of the latest evaluation run aggregated by module and category.
/// </summary>
public sealed record EvaluationSummaryReport(
    int TotalScenarios,
    int Passed,
    int Failed,
    double PassRate,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Categories,
    DateTime GeneratedAtUtc);
