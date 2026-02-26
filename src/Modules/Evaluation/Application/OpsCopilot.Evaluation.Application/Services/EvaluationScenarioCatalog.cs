using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Services;

/// <summary>
/// Holds all registered <see cref="IEvaluationScenario"/> instances and exposes
/// their metadata as <see cref="EvaluationScenario"/> records.
/// </summary>
public sealed class EvaluationScenarioCatalog
{
    private readonly IReadOnlyList<IEvaluationScenario> _scenarios;

    public EvaluationScenarioCatalog(IEnumerable<IEvaluationScenario> scenarios)
    {
        _scenarios = scenarios.ToList().AsReadOnly();
    }

    /// <summary>Scenario implementations (for execution).</summary>
    public IReadOnlyList<IEvaluationScenario> Scenarios => _scenarios;

    /// <summary>Read-only metadata projection.</summary>
    public IReadOnlyList<EvaluationScenario> GetMetadata() =>
        _scenarios.Select(s => new EvaluationScenario(
            s.ScenarioId, s.Module, s.Name, s.Category, s.Description))
        .ToList()
        .AsReadOnly();
}
