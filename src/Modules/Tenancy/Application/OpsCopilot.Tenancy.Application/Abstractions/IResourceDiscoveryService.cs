using OpsCopilot.Tenancy.Application.DTOs;

namespace OpsCopilot.Tenancy.Application.Abstractions;

/// <summary>
/// Discovers Azure resources and connected dependencies for a tenant.
/// §6.19 — Onboarding Orchestration (Resource Graph discovery + dependency sampling).
/// </summary>
public interface IResourceDiscoveryService
{
    Task<ResourceDiscoverySummary> DiscoverAsync(Guid tenantId, CancellationToken ct = default);
}
