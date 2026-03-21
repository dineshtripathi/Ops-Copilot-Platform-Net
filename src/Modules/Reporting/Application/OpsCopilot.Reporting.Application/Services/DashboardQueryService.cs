using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Services;

/// <summary>
/// Composes the three <see cref="IAgentRunsReportingQueryService"/> queries in parallel
/// and returns a single <see cref="DashboardOverviewResponse"/> scoped to one tenant.
/// </summary>
public sealed class DashboardQueryService : IDashboardQueryService
{
    private readonly IAgentRunsReportingQueryService _agentRuns;

    public DashboardQueryService(IAgentRunsReportingQueryService agentRuns)
    {
        _agentRuns = agentRuns;
    }

    public async Task<DashboardOverviewResponse> GetOverviewAsync(
        DateTime?         fromUtc,
        DateTime?         toUtc,
        string            tenantId,
        string?           status,
        string?           sort,
        int?              limit,
        CancellationToken ct)
    {
        // Sequential awaits required: all four query methods share one scoped
        // ReportingReadDbContext instance; Task.WhenAll would trigger multiple
        // concurrent operations on the same context, which EF Core does not allow.
        var summary    = await _agentRuns.GetSummaryAsync(fromUtc, toUtc, tenantId, ct);
        var trend      = await _agentRuns.GetTrendAsync(fromUtc, toUtc, tenantId, ct);
        var tools      = await _agentRuns.GetToolUsageAsync(fromUtc, toUtc, tenantId, ct);
        var recentRuns = await _agentRuns.GetRecentRunsAsync(tenantId, limit ?? 10, status, sort, ct);
        var exceptionTrend = await _agentRuns.GetExceptionTrendAsync(fromUtc, toUtc, tenantId, ct);
        var hotResources = await _agentRuns.GetHotResourcesAsync(fromUtc, toUtc, tenantId, maxCount: 8, ct);
        var blastRadius = await _agentRuns.GetBlastRadiusAsync(fromUtc, toUtc, tenantId, ct);
        var topDiagnosis = await _agentRuns.GetTopDiagnosisAsync(fromUtc, toUtc, tenantId, maxCount: 3, ct);
        var deploymentCorrelation = await _agentRuns.GetDeploymentCorrelationAsync(fromUtc, toUtc, tenantId, ct);
        var activitySignals = await _agentRuns.GetActivitySignalsAsync(fromUtc, toUtc, tenantId, ct);

        return new DashboardOverviewResponse(
            Summary: summary,
            Trend: trend,
            TopTools: tools,
            RecentRuns: recentRuns,
            ExceptionTrend: exceptionTrend,
            DeploymentCorrelation: deploymentCorrelation,
            HotResources: hotResources,
            BlastRadius: blastRadius,
            ActivitySignals: activitySignals,
            TopDiagnosis: topDiagnosis);
    }
}
