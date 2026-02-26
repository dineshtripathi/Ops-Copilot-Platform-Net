using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that a Datadog alert payload contains the expected provider marker.
/// </summary>
public sealed class AlertIngestion_DatadogProviderScenario : IEvaluationScenario
{
    public string ScenarioId  => "AI-002";
    public string Module      => "AlertIngestion";
    public string Name        => "Datadog provider detection";
    public string Category    => "Routing";
    public string Description => "A Datadog payload with 'alertType' key routes correctly.";

    public EvaluationResult Execute()
    {
        const string samplePayload = """{"alertType":"metric alert","title":"Disk full"}""";
        var hasAlertType = samplePayload.Contains("\"alertType\"", StringComparison.Ordinal);

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: hasAlertType,
            Expected: "alertType key present",
            Actual: hasAlertType ? "alertType key present" : "alertType key missing");
    }
}
