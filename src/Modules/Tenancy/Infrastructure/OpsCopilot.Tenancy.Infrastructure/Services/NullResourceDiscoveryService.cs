using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Null implementation of <see cref="IResourceDiscoveryService"/>.
/// Returns an empty summary — safe default when no ARG integration is configured.
/// §6.19 — Onboarding Orchestration.
/// </summary>
public sealed class NullResourceDiscoveryService : IResourceDiscoveryService
{
    public Task<ResourceDiscoverySummary> DiscoverAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(new ResourceDiscoverySummary(tenantId, 0, Array.Empty<string>()));
}
