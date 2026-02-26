using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Abstractions;

/// <summary>
/// A single deterministic evaluation scenario.
/// Implementations MUST NOT perform I/O or depend on external state.
/// </summary>
public interface IEvaluationScenario
{
    string   ScenarioId  { get; }
    string   Module      { get; }
    string   Name        { get; }
    string   Category    { get; }
    string   Description { get; }

    EvaluationResult Execute();
}
