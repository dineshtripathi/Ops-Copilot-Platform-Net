using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsCopilot.Tenancy.Application.Abstractions;
using OpsCopilot.Tenancy.Application.DTOs;
using OpsCopilot.Tenancy.Infrastructure.Persistence;

namespace OpsCopilot.Tenancy.Infrastructure.Services;

/// <summary>
/// Derives resource discovery results from the tenant's config entries stored in SQL.
/// Config-entry-based approach (no ARM SDK — module boundary prohibits Azure.ResourceManager references).
/// </summary>
internal sealed class ArmResourceDiscoveryService(
    TenancyDbContext db,
    ILogger<ArmResourceDiscoveryService> logger) : IResourceDiscoveryService
{
    // Maps a substring found in a config-entry key → the connector name it implies.
    private static readonly IReadOnlyDictionary<string, string> ConnectorKeyMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppInsights"]  = "app-insights",
            ["LogAnalytics"] = "log-analytics",
            ["ServiceBus"]   = "service-bus",
            ["Subscription"] = "arm",
        };

    public async Task<ResourceDiscoverySummary> DiscoverAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            var keys = await db.TenantConfigEntries
                .AsNoTracking()
                .Where(e => e.TenantId == tenantId)
                .Select(e => e.Key)
                .ToListAsync(ct);

            var detected = keys
                .SelectMany(key => ConnectorKeyMap
                    .Where(kv => key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation(
                "Resource discovery for tenant {TenantId}: {EntryCount} config entries, {ConnectorCount} connector(s) detected.",
                tenantId, keys.Count, detected.Count);

            return new ResourceDiscoverySummary(tenantId, keys.Count, detected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Resource discovery failed for tenant {TenantId}; returning empty summary.",
                tenantId);
            return new ResourceDiscoverySummary(tenantId, 0, Array.Empty<string>());
        }
    }
}
