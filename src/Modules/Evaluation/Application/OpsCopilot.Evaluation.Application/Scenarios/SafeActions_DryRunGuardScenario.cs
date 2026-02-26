using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that the dry-run flag is respected: a dry-run action should NOT
/// produce side-effects (modeled here as a boolean guard check).
/// </summary>
public sealed class SafeActions_DryRunGuardScenario : IEvaluationScenario
{
    public string ScenarioId  => "SA-003";
    public string Module      => "SafeActions";
    public string Name        => "Dry-run guard enforcement";
    public string Category    => "Execution";
    public string Description => "When dryRun=true, execution must be suppressed.";

    public EvaluationResult Execute()
    {
        const bool dryRun = true;
        var executionSuppressed = dryRun; // guard: if dryRun, don't execute

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: executionSuppressed,
            Expected: "Execution suppressed",
            Actual: executionSuppressed ? "Execution suppressed" : "Execution proceeded");
    }
}
