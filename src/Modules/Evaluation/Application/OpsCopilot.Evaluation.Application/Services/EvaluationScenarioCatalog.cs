using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Services;

/// <summary>
/// Holds all registered <see cref="IEvaluationScenario"/> and <see cref="ILlmGradedScenario"/>
/// instances and exposes their metadata as <see cref="EvaluationScenario"/> records.
/// </summary>
public sealed class EvaluationScenarioCatalog
{
    private readonly IReadOnlyList<IEvaluationScenario> _scenarios;
    private readonly IReadOnlyList<ILlmGradedScenario> _llmGradedScenarios;

    public EvaluationScenarioCatalog(
        IEnumerable<IEvaluationScenario> scenarios,
        IEnumerable<ILlmGradedScenario> llmGradedScenarios)
    {
        _scenarios = scenarios.ToList().AsReadOnly();
        _llmGradedScenarios = llmGradedScenarios.ToList().AsReadOnly();
    }

    /// <summary>Deterministic scenario implementations (for execution).</summary>
    public IReadOnlyList<IEvaluationScenario> Scenarios => _scenarios;

    /// <summary>LLM-graded scenario implementations (for async execution).</summary>
    public IReadOnlyList<ILlmGradedScenario> LlmGradedScenarios => _llmGradedScenarios;

    /// <summary>Combined metadata for all scenario types.</summary>
    public IReadOnlyList<EvaluationScenario> GetAllScenarios()
    {
        var deterministic = _scenarios.Select(s => new EvaluationScenario(
            s.ScenarioId, s.Module, s.Name, s.Category, s.Description));
        var llmGraded = _llmGradedScenarios.Select(s => new EvaluationScenario(
            s.ScenarioId, s.Module, s.Name, s.Category, s.Description));
        return deterministic.Concat(llmGraded).ToList().AsReadOnly();
    }

    /// <summary>Read-only metadata projection (deterministic only, for backwards compat).</summary>
    public IReadOnlyList<EvaluationScenario> GetMetadata() =>
        _scenarios.Select(s => new EvaluationScenario(
            s.ScenarioId, s.Module, s.Name, s.Category, s.Description))
        .ToList()
        .AsReadOnly();
}
