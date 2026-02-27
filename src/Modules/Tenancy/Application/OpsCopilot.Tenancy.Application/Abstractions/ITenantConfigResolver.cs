using OpsCopilot.Tenancy.Application.DTOs;

namespace OpsCopilot.Tenancy.Application.Abstractions;

public interface ITenantConfigResolver
{
    Task<EffectiveTenantConfig> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
