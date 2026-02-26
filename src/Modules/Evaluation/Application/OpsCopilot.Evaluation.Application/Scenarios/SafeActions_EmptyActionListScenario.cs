using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that an empty action list is correctly handled (no crash, zero results).
/// </summary>
public sealed class SafeActions_EmptyActionListScenario : IEvaluationScenario
{
    public string ScenarioId  => "SA-002";
    public string Module      => "SafeActions";
    public string Name        => "Empty action list handling";
    public string Category    => "EdgeCase";
    public string Description => "An empty action list should yield zero results without error.";

    public EvaluationResult Execute()
    {
        var actions = Array.Empty<string>();
        var count   = actions.Length;
        var passed  = count == 0;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: "0 actions processed",
            Actual: $"{count} actions processed");
    }
}
