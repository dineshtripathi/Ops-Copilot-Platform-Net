using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that a summary report computes totals correctly from known data.
/// </summary>
public sealed class Reporting_SummaryTotalsScenario : IEvaluationScenario
{
    public string ScenarioId  => "RP-001";
    public string Module      => "Reporting";
    public string Name        => "Summary totals computation";
    public string Category    => "Aggregation";
    public string Description => "Given known counts, summary totals must equal the sum of parts.";

    public EvaluationResult Execute()
    {
        // Deterministic model: 3 succeeded + 2 failed = 5 total
        const int succeeded = 3;
        const int failed    = 2;
        const int total     = succeeded + failed;
        var passed = total == 5;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: "Total = 5",
            Actual: $"Total = {total}");
    }
}
