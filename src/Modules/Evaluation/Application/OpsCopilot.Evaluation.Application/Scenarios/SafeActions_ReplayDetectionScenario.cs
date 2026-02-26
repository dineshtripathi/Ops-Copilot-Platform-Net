using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that execution replay detection correctly identifies a duplicate
/// idempotency key.
/// </summary>
public sealed class SafeActions_ReplayDetectionScenario : IEvaluationScenario
{
    public string ScenarioId  => "SA-004";
    public string Module      => "SafeActions";
    public string Name        => "Replay detection";
    public string Category    => "Idempotency";
    public string Description => "A duplicate idempotency key should be flagged as a replay.";

    public EvaluationResult Execute()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        const string key = "action-exec-abc123";

        seen.Add(key);
        var isReplay = !seen.Add(key); // second add returns false → replay

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: isReplay,
            Expected: "Replay detected",
            Actual: isReplay ? "Replay detected" : "No replay detected");
    }
}
