using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

/// <summary>
/// Aggregates tenant-scoped reporting data into a single dashboard overview.
/// Composes <see cref="IAgentRunsReportingQueryService"/> — no direct infrastructure coupling.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Returns a composite dashboard overview for the given tenant and optional date window.
    /// </summary>
    Task<DashboardOverviewResponse> GetOverviewAsync(
        DateTime?         fromUtc,
        DateTime?         toUtc,
        string            tenantId,
        string?           status,
        string?           sort,
        int?              limit,
        CancellationToken ct);
}
