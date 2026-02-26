namespace OpsCopilot.Evaluation.Domain.Models;

/// <summary>
/// Aggregated summary returned by GET /evaluation/run.
/// </summary>
public sealed record EvaluationRunSummary(
    Guid                              RunId,
    DateTime                          RanAtUtc,
    int                               TotalScenarios,
    int                               Passed,
    int                               Failed,
    IReadOnlyList<EvaluationResult>   Results);
