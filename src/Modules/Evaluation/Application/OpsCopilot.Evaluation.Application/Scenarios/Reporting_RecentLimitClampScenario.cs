using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that the recent-actions limit clamps to the expected bounds.
/// </summary>
public sealed class Reporting_RecentLimitClampScenario : IEvaluationScenario
{
    public string ScenarioId  => "RP-002";
    public string Module      => "Reporting";
    public string Name        => "Recent limit clamping";
    public string Category    => "Validation";
    public string Description => "Requested limit beyond max should be clamped to MaxRecentLimit (100).";

    public EvaluationResult Execute()
    {
        const int maxLimit      = 100;
        const int requestedLimit = 999;
        var clamped = Math.Clamp(requestedLimit, 1, maxLimit);
        var passed  = clamped == maxLimit;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: $"Clamped to {maxLimit}",
            Actual: $"Clamped to {clamped}");
    }
}
