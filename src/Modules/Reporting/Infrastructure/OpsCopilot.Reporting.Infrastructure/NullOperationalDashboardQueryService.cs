using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Infrastructure;

internal sealed class NullOperationalDashboardQueryService : IOperationalDashboardQueryService
{
    public Task<IReadOnlyList<MttrTrendPoint>> GetMttrTrendAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MttrTrendPoint>>(Array.Empty<MttrTrendPoint>());

    public Task<VerifiedSavingsReport> GetVerifiedSavingsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        CancellationToken ct)
        => Task.FromResult(new VerifiedSavingsReport(0m, 0, null, null));

    public Task<IReadOnlyList<IncidentCategoryRow>> GetTopIncidentCategoriesAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        int       maxCount,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<IncidentCategoryRow>>(Array.Empty<IncidentCategoryRow>());
}
