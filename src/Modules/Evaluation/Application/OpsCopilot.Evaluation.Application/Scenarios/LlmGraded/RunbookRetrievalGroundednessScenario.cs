using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Application.Services;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios.LlmGraded;

public sealed class RunbookRetrievalGroundednessScenario : ILlmGradedScenario
{
    private const float PassThreshold = 0.3f;
    private readonly RelevanceScorer _scorer;

    public RunbookRetrievalGroundednessScenario(RelevanceScorer scorer)
    {
        _scorer = scorer;
    }

    public string ScenarioId  => "LG-002";
    public string Module      => "Rag";
    public string Name        => "Runbook retrieval relevance";
    public string Category    => "LlmGraded";
    public string Description => "Measures whether the retrieved runbook is relevant to the alert.";

    public async Task<EvaluationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        const string alertDescription =
            "High CPU alert on production VM web-prod-01 in East US region.";

        const string retrievedRunbook =
            "Runbook: High CPU Troubleshooting — Check recent deployments, " +
            "review auto-scaling configuration, inspect top processes, " +
            "verify load balancer health probes.";

        var score = await _scorer.ScoreAsync(alertDescription, retrievedRunbook, cancellationToken);
        var passed = score >= PassThreshold;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: $"Relevance >= {PassThreshold:F2}",
            Actual: $"Relevance = {score:F4}",
            Score: score);
    }
}
