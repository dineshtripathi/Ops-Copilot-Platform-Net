using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Read-only query service for safe-actions reporting.
/// </summary>
public interface IReportingQueryService
{
    Task<SafeActionsSummaryReport> GetSummaryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct);

    Task<IReadOnlyList<ActionTypeBreakdownRow>> GetByActionTypeAsync(
        DateTime? fromUtc, DateTime? toUtc, string? tenantId, CancellationToken ct);

    Task<IReadOnlyList<TenantBreakdownRow>> GetByTenantAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct);

    Task<IReadOnlyList<RecentActionRow>> GetRecentAsync(
        int limit, string? tenantId, CancellationToken ct);
}
