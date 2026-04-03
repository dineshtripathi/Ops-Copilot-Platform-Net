namespace OpsCopilot.Reporting.Domain.Models;

/// <summary>
/// Composite dashboard overview: agent-run summary, trend line, top-tool breakdown,
/// and recent run list. All fields are derived from existing read models — no fabricated metrics.
/// </summary>
public sealed record DashboardOverviewResponse(
    AgentRunsSummaryReport              Summary,
    IReadOnlyList<AgentRunsTrendPoint>   Trend,
    IReadOnlyList<ToolUsageSummaryRow>   TopTools,
    IReadOnlyList<RecentRunSummary>      RecentRuns,
    IReadOnlyList<ExceptionTrendPoint>?  ExceptionTrend = null,
    IReadOnlyList<DeploymentCorrelationPoint>? DeploymentCorrelation = null,
    IReadOnlyList<HotResourceRow>?       HotResources = null,
    BlastRadiusSummary?                  BlastRadius = null,
    ActivitySignalSummary?               ActivitySignals = null,
    IReadOnlyList<DiagnosisHypothesis>?  TopDiagnosis = null,
    ObservabilityEvidenceSpotlight?      ObservabilitySpotlight = null,
    ObservabilityEvidenceSummary?        LiveObservabilityEvidence = null,
    LiveImpactEvidenceSummary?           LiveImpactEvidence = null,
    TenantEstateSummary?                 TenantEstate = null,
    TenantResourceInventory?             ResourceInventory = null,
    DashboardDataFreshness?              DataFreshness = null);
