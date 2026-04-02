namespace OpsCopilot.Evaluation.Domain.Models;

public sealed record AsyncEvaluationRun(
    Guid                    RunId,
    EvaluationRunStatus     Status,
    DateTime                StartedAtUtc,
    EvaluationRunSummary?   Summary = null);
