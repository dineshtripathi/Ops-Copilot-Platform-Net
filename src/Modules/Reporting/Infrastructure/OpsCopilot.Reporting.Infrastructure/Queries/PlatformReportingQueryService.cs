using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Evaluation.Application.Services;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.Reporting.Infrastructure.Queries;

/// <summary>
/// Composes read-only platform-level reports from Evaluation, Connector, and SafeActions data.
/// All data is computed in-memory — no database access.
/// </summary>
internal sealed class PlatformReportingQueryService : IPlatformReportingQueryService
{
    private readonly EvaluationRunner _runner;
    private readonly EvaluationScenarioCatalog _catalog;
    private readonly IConnectorRegistry _connectors;
    private readonly IActionTypeCatalog _actionTypes;

    public PlatformReportingQueryService(
        EvaluationRunner runner,
        EvaluationScenarioCatalog catalog,
        IConnectorRegistry connectors,
        IActionTypeCatalog actionTypes)
    {
        _runner = runner;
        _catalog = catalog;
        _connectors = connectors;
        _actionTypes = actionTypes;
    }

    public Task<EvaluationSummaryReport> GetEvaluationSummaryAsync(CancellationToken ct)
    {
        var run = _runner.Run();
        var metadata = _catalog.GetMetadata();

        var modules = metadata.Select(m => m.Module).Distinct().Order().ToList().AsReadOnly();
        var categories = metadata.Select(m => m.Category).Distinct().Order().ToList().AsReadOnly();
        var passRate = run.TotalScenarios > 0
            ? Math.Round((double)run.Passed / run.TotalScenarios * 100, 2)
            : 0d;

        var report = new EvaluationSummaryReport(
            TotalScenarios: run.TotalScenarios,
            Passed: run.Passed,
            Failed: run.Failed,
            PassRate: passRate,
            Modules: modules,
            Categories: categories,
            GeneratedAtUtc: DateTime.UtcNow);

        return Task.FromResult(report);
    }

    public Task<ConnectorInventoryReport> GetConnectorInventoryAsync(CancellationToken ct)
    {
        var all = _connectors.ListAll();

        var byKind = all
            .GroupBy(c => c.Kind.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        var rows = all
            .Select(c => new ConnectorInventoryRow(c.Name, c.Kind.ToString(), c.Description, c.Capabilities))
            .ToList()
            .AsReadOnly();

        var report = new ConnectorInventoryReport(
            TotalConnectors: all.Count,
            ByKind: byKind,
            Connectors: rows,
            GeneratedAtUtc: DateTime.UtcNow);

        return Task.FromResult(report);
    }

    public Task<PlatformReadinessReport> GetPlatformReadinessAsync(CancellationToken ct)
    {
        var run = _runner.Run();
        var connectorCount = _connectors.ListAll().Count;
        var actionTypeCount = _actionTypes.List().Count;

        var passRate = run.TotalScenarios > 0
            ? Math.Round((double)run.Passed / run.TotalScenarios * 100, 2)
            : 0d;

        var report = new PlatformReadinessReport(
            EvaluationPassRate: passRate,
            TotalConnectors: connectorCount,
            TotalActionTypes: actionTypeCount,
            AllEvaluationsPassing: run.Failed == 0 && run.TotalScenarios > 0,
            GeneratedAtUtc: DateTime.UtcNow);

        return Task.FromResult(report);
    }
}
