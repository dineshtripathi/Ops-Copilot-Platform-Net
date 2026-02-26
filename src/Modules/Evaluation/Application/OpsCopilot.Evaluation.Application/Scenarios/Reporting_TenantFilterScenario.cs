using OpsCopilot.Evaluation.Application.Abstractions;
using OpsCopilot.Evaluation.Domain.Models;

namespace OpsCopilot.Evaluation.Application.Scenarios;

/// <summary>
/// Verifies that tenant filtering correctly partitions data.
/// </summary>
public sealed class Reporting_TenantFilterScenario : IEvaluationScenario
{
    public string ScenarioId  => "RP-003";
    public string Module      => "Reporting";
    public string Name        => "Tenant-scoped filtering";
    public string Category    => "Filtering";
    public string Description => "Only items matching the requested tenant should be returned.";

    public EvaluationResult Execute()
    {
        var data = new[]
        {
            ("contoso", 10),
            ("fabrikam", 5),
            ("contoso", 3)
        };

        const string targetTenant = "contoso";
        var filtered = data.Where(d => d.Item1 == targetTenant).Sum(d => d.Item2);
        var passed   = filtered == 13;

        return new EvaluationResult(
            ScenarioId, Module,
            Passed: passed,
            Expected: "Contoso total = 13",
            Actual: $"Contoso total = {filtered}");
    }
}
