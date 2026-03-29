using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Services;

/// <summary>
/// Composes the three <see cref="IAgentRunsReportingQueryService"/> queries in parallel
/// and returns a single <see cref="DashboardOverviewResponse"/> scoped to one tenant.
/// </summary>
public sealed class DashboardQueryService : IDashboardQueryService
{
    private static readonly TimeSpan HistoricalStalenessThreshold = TimeSpan.FromHours(6);

    private readonly IAgentRunsReportingQueryService _agentRuns;
    private readonly IObservabilityEvidenceProvider _observability;
    private readonly ITenantEstateProvider _tenantEstate;
    private readonly ITenantResourceInventoryProvider _resourceInventory;

    public DashboardQueryService(
        IAgentRunsReportingQueryService agentRuns,
        IObservabilityEvidenceProvider observability,
        ITenantEstateProvider tenantEstate,
        ITenantResourceInventoryProvider resourceInventory)
    {
        _agentRuns = agentRuns;
        _observability = observability;
        _tenantEstate = tenantEstate;
        _resourceInventory = resourceInventory;
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
        var recentRuns = await _agentRuns.GetRecentRunsAsync(tenantId, limit ?? 10, status, sort, fromUtc, toUtc, ct);
        var exceptionTrend = await _agentRuns.GetExceptionTrendAsync(fromUtc, toUtc, tenantId, ct);
        var hotResources = await _agentRuns.GetHotResourcesAsync(fromUtc, toUtc, tenantId, maxCount: 8, ct);
        var blastRadius = await _agentRuns.GetBlastRadiusAsync(fromUtc, toUtc, tenantId, ct);
        var topDiagnosis = await _agentRuns.GetTopDiagnosisAsync(fromUtc, toUtc, tenantId, maxCount: 3, ct);
        var deploymentCorrelation = await _agentRuns.GetDeploymentCorrelationAsync(fromUtc, toUtc, tenantId, ct);
        var activitySignals = await _agentRuns.GetActivitySignalsAsync(fromUtc, toUtc, tenantId, ct);
        var observabilitySpotlight = await _agentRuns.GetObservabilitySpotlightAsync(fromUtc, toUtc, tenantId, ct);
        // The three external-service calls below (estate, inventory, observability) do not
        // share the EF Core DbContext, so they can be parallelised safely.
        // ObservabilityEvidenceProvider.GetLiveCombinedAsync runs the evidence pack once and
        // splits the result into both summaries, avoiding a redundant second pack execution.
        var tenantEstateTask      = _tenantEstate.GetTenantEstateSummaryAsync(tenantId, ct);
        var resourceInventoryTask = _resourceInventory.GetInventoryAsync(tenantId, ct);
        var liveCombinedTask      = _observability.GetLiveCombinedAsync(tenantId, fromUtc, toUtc, ct);

        await Task.WhenAll(tenantEstateTask, resourceInventoryTask, liveCombinedTask);

        var tenantEstate              = tenantEstateTask.Result;
        var resourceInventory         = resourceInventoryTask.Result;
        var (liveObservabilityEvidence, liveImpactEvidence) = liveCombinedTask.Result;
        var latestHistoricalRunAtUtc = recentRuns.Count > 0
            ? recentRuns.Max(r => r.CreatedAtUtc)
            : (DateTimeOffset?)null;
        var liveEvaluatedAtUtc = DateTimeOffset.UtcNow;
        var dataFreshness = new DashboardDataFreshness(
            LiveEvaluatedAtUtc: liveEvaluatedAtUtc,
            LatestHistoricalRunAtUtc: latestHistoricalRunAtUtc,
            HistoricalDataIsStale: !latestHistoricalRunAtUtc.HasValue ||
                                  (liveEvaluatedAtUtc - latestHistoricalRunAtUtc.Value) > HistoricalStalenessThreshold);

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
            TopDiagnosis: topDiagnosis,
            ObservabilitySpotlight: observabilitySpotlight,
            LiveObservabilityEvidence: liveObservabilityEvidence,
            LiveImpactEvidence: liveImpactEvidence,
            TenantEstate: tenantEstate,
            ResourceInventory: resourceInventory,
            DataFreshness: dataFreshness);
    }
}
