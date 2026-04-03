using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Application.Services;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios.LlmGraded;

public sealed class TriageResponseGroundednessScenario : ILlmGradedScenario
{
    private const float PassThreshold = 0.3f;
    private readonly GroundednessScorer _scorer;

    public TriageResponseGroundednessScenario(GroundednessScorer scorer)
    {
        _scorer = scorer;
    }

    public string ScenarioId  => "LG-001";
    public string Module      => "Triage";
    public string Name        => "Triage response groundedness";
    public string Category    => "LlmGraded";
    public string Description => "Measures whether a triage response is grounded in the provided evidence.";

    public async Task<EvaluationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        const string evidence =
            "The VM web-prod-01 has 98% CPU utilization. The alert fired at 14:32 UTC. " +
            "Recent deployment changed auto-scaling rules. Memory is at 45%.";

        const string triageResponse =
            "The high CPU on web-prod-01 is likely caused by the recent deployment " +
            "that modified auto-scaling rules. CPU is at 98% while memory remains normal at 45%.";

        var score = await _scorer.ScoreAsync(evidence, triageResponse, cancellationToken);
        var passed = score >= PassThreshold;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: $"Groundedness >= {PassThreshold:F2}",
            Actual: $"Groundedness = {score:F4}",
            Score: score);
    }
}
