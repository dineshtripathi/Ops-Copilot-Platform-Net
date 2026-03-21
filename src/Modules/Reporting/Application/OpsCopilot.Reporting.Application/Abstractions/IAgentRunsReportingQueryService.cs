using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Read-only query service for agent-runs reporting.
/// All methods derive metrics from the stored agent-run ledger only.
/// </summary>
public interface IAgentRunsReportingQueryService
{
    Task<AgentRunsSummaryReport> GetSummaryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct);

    Task<IReadOnlyList<AgentRunsTrendPoint>> GetTrendAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct);

    Task<IReadOnlyList<ToolUsageSummaryRow>> GetToolUsageAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct);

    Task<IReadOnlyList<RecentRunSummary>> GetRecentRunsAsync(
        string tenantId, int maxCount, string? status, string? sort, CancellationToken ct);

    /// <summary>
    /// Slice 66: Returns detail for a single run scoped to the tenant.
    /// Returns null for both "not found" and "wrong tenant" — no cross-tenant oracle.
    /// </summary>
    Task<RunDetailResponse?> GetRunDetailAsync(
        Guid runId, string tenantId, CancellationToken ct);

    /// <summary>
    /// Slice 67: Returns all runs for a session, scoped to the tenant.
    /// Returns null for both "not found" and "wrong tenant" — no cross-tenant oracle.
    /// </summary>
    Task<SessionDetailResponse?> GetSessionDetailAsync(
        Guid sessionId, string tenantId, CancellationToken ct);
}
