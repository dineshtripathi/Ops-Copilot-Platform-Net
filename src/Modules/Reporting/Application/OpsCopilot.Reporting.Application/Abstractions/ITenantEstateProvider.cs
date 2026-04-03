using OpsCopilot.Reporting.Domain.Models;

namespace OpsCopilot.Reporting.Application.Abstractions;

public interface ITenantEstateProvider
{
    Task<TenantEstateSummary?> GetTenantEstateSummaryAsync(string tenantId, CancellationToken ct);
}