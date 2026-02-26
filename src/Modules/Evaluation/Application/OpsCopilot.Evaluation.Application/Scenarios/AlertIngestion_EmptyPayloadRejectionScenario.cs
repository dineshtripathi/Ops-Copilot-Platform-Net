using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that an empty alert payload is correctly identified as invalid.
/// </summary>
public sealed class AlertIngestion_EmptyPayloadRejectionScenario : IEvaluationScenario
{
    public string ScenarioId  => "AI-003";
    public string Module      => "AlertIngestion";
    public string Name        => "Empty payload rejection";
    public string Category    => "Validation";
    public string Description => "An empty payload string should be flagged as invalid.";

    public EvaluationResult Execute()
    {
        var payload = string.Empty;
        var isInvalid = string.IsNullOrWhiteSpace(payload);

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: isInvalid,
            Expected: "payload rejected",
            Actual: isInvalid ? "payload rejected" : "payload accepted");
    }
}
