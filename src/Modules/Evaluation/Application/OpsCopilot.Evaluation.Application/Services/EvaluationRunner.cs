using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Services;

/// <summary>
/// Executes every registered scenario and produces an <see cref="EvaluationRunSummary"/>.
/// Deterministic, no I/O, no persistence.
/// </summary>
public sealed class EvaluationRunner
{
    private readonly EvaluationScenarioCatalog _catalog;

    public EvaluationRunner(EvaluationScenarioCatalog catalog)
    {
        _catalog = catalog;
    }

    public EvaluationRunSummary Run()
    {
        var results = _catalog.Scenarios
            .Select(s => s.Execute())
            .ToList()
            .AsReadOnly();

        return new EvaluationRunSummary(
            RunId: Guid.NewGuid(),
            RanAtUtc: DateTime.UtcNow,
            TotalScenarios: results.Count,
            Passed: results.Count(r => r.Passed),
            Failed: results.Count(r => !r.Passed),
            Results: results);
    }
}
