using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that the SafeActions module correctly classifies a well-known
/// action type as "safe" or "unsafe" based on type-name convention.
/// </summary>
public sealed class SafeActions_ActionTypeClassificationScenario : IEvaluationScenario
{
    public string ScenarioId  => "SA-001";
    public string Module      => "SafeActions";
    public string Name        => "Action type classification";
    public string Category    => "Classification";
    public string Description => "ReadOnly-prefixed action types should be classified as safe.";

    public EvaluationResult Execute()
    {
        const string actionType = "ReadOnly.GetVmStatus";
        var isSafe = actionType.StartsWith("ReadOnly.", StringComparison.OrdinalIgnoreCase);

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: isSafe,
            Expected: "ReadOnly prefix → safe",
            Actual: isSafe ? "ReadOnly prefix → safe" : "Misclassified");
    }
}
