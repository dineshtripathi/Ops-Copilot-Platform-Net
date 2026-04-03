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

    public async Task<EvaluationRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var deterministicResults = _catalog.Scenarios
            .Select(s => s.Execute())
            .ToList();

        var llmTasks = _catalog.LlmGradedScenarios
            .Select(s => s.ExecuteAsync(cancellationToken));
        var llmResults = await Task.WhenAll(llmTasks);

        var allResults = deterministicResults.Concat(llmResults).ToList().AsReadOnly();

        return new EvaluationRunSummary(
            RunId: Guid.NewGuid(),
            RanAtUtc: DateTime.UtcNow,
            TotalScenarios: allResults.Count,
            Passed: allResults.Count(r => r.Passed),
            Failed: allResults.Count(r => !r.Passed),
            Results: allResults);
    }
}
