using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

public interface IOperationalDashboardQueryService
{
    Task<IReadOnlyList<MttrTrendPoint>> GetMttrTrendAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        CancellationToken ct);

    Task<VerifiedSavingsReport> GetVerifiedSavingsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        CancellationToken ct);

    Task<IReadOnlyList<IncidentCategoryRow>> GetTopIncidentCategoriesAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string?   tenantId,
        int       maxCount,
        CancellationToken ct);
}
