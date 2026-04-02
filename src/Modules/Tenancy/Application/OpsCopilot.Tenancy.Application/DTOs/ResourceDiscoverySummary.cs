namespace OpsCopilot.Tenancy.Application.DTOs;

/// <summary>
/// Result of an Azure Resource Graph discovery run for a tenant.
/// §6.19 — Onboarding Orchestration (Resource Graph discovery + dependency sampling).
/// </summary>
public sealed record ResourceDiscoverySummary(
    Guid TenantId,
    int DiscoveredResourceCount,
    IReadOnlyList<string> DetectedConnectors);
