namespace OpsCopilot.Evaluation.Domain.Models;

/// <summary>
/// Describes an evaluation scenario — metadata only, no execution logic.
/// </summary>
public sealed record EvaluationScenario(
    string ScenarioId,
    string Module,
    string Name,
    string Category,
    string Description);
