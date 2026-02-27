using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Read-only query service that aggregates cross-module platform-level reports
/// (evaluation, connectors, action-type catalog).
/// </summary>
public interface IPlatformReportingQueryService
{
    Task<EvaluationSummaryReport> GetEvaluationSummaryAsync(CancellationToken ct);
    Task<ConnectorInventoryReport> GetConnectorInventoryAsync(CancellationToken ct);
    Task<PlatformReadinessReport> GetPlatformReadinessAsync(CancellationToken ct);
}
