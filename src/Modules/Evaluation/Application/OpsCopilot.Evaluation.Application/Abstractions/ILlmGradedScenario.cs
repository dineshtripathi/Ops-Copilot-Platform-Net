using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Abstractions;

/// <summary>
/// An LLM-graded evaluation scenario that scores triage response quality.
/// Implementations may perform async I/O (embedding generation).
/// </summary>
public interface ILlmGradedScenario
{
    string   ScenarioId  { get; }
    string   Module      { get; }
    string   Name        { get; }
    string   Category    { get; }
    string   Description { get; }

    Task<EvaluationResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
