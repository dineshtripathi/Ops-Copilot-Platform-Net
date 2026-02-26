namespace OpsCopilot.Evaluation.Domain.Models;

/// <summary>
/// The outcome of executing a single evaluation scenario.
/// </summary>
public sealed record EvaluationResult(
    string ScenarioId,
    string Module,
    bool   Passed,
    string Expected,
    string Actual,
    string? Reason = null);
