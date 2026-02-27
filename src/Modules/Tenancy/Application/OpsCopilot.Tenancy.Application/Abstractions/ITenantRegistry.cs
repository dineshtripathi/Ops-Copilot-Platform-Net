using OpsCopilot.Tenancy.Domain.Entities;

namespace OpsCopilot.Tenancy.Application.Abstractions;

public interface ITenantRegistry
{
    Task<Tenant> CreateAsync(string displayName, string? updatedBy, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default);
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
}
