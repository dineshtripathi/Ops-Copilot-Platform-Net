using OpsCopilot.Tenancy.Domain.Entities;

namespace OpsCopilot.Tenancy.Application.Abstractions;

public interface ITenantConfigStore
{
    Task UpsertAsync(Guid tenantId, string key, string value, string? updatedBy, CancellationToken ct = default);
    Task<IReadOnlyList<TenantConfigEntry>> GetAsync(Guid tenantId, CancellationToken ct = default);
}
