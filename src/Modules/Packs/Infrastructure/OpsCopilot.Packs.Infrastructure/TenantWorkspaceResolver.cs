using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Resolves the Log Analytics workspace ID for a tenant by reading from configuration.
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>Tenant-specific key: <c>Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId</c></item>
///   <item>Global fallback key: <c>Observability:LogAnalyticsWorkspaceId</c></item>
///   <item>Legacy fallback key: <c>WORKSPACE_ID</c></item>
/// </list>
/// </para>
/// <para>
/// After resolving a GUID, the workspace is validated against
/// <c>SafeActions:AllowedLogAnalyticsWorkspaceIds</c> when that list is non-empty.
/// </para>
/// <para>
/// When an <see cref="IObservabilityResourceDiscovery"/> is available, <see cref="ResolveAsync"/>
/// enriches the result with the linked App Insights resource path discovered via ARM.
/// </para>
/// </summary>
internal sealed class TenantWorkspaceResolver : ITenantWorkspaceResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantWorkspaceResolver> _logger;
    private readonly IObservabilityResourceDiscovery? _resourceDiscovery;

    // Populated on first discovery call; reused for all subsequent resolutions.
    // Volatile ensures the reference write is visible across threads.
    // Benign race: two concurrent cold-start calls build equivalent caches; last writer wins.
    private volatile WorkspaceDiscoveryCache? _cache;

    public TenantWorkspaceResolver(
        IConfiguration configuration,
        ILogger<TenantWorkspaceResolver> logger,
        IObservabilityResourceDiscovery? resourceDiscovery = null)
    {
        _configuration     = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger            = logger        ?? throw new ArgumentNullException(nameof(logger));
        _resourceDiscovery = resourceDiscovery;
    }

    /// <inheritdoc />
    public WorkspaceResolutionResult Resolve(string tenantId)
    {
        // ── 1. Locate workspace ID in config ────────────────────────────────
        var workspaceId =
            _configuration[$"Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId"]
            ?? _configuration["Observability:LogAnalyticsWorkspaceId"]
            ?? _configuration["WORKSPACE_ID"];

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            _logger.LogDebug(
                "[WorkspaceResolver] No workspace configured for tenant {TenantId}",
                tenantId);
            return new WorkspaceResolutionResult(false, null, "missing_workspace");
        }

        // ── 2. Validate GUID ────────────────────────────────────────────────
        if (!Guid.TryParse(workspaceId, out _))
        {
            _logger.LogWarning(
                "[WorkspaceResolver] Invalid workspace GUID for tenant {TenantId} — value is not a GUID",
                tenantId);
            return new WorkspaceResolutionResult(false, null, "missing_workspace");
        }

        // ── 3. Allowlist check (only enforced when list is non-empty) ────────
        var allowlist = _configuration
            .GetSection("SafeActions:AllowedLogAnalyticsWorkspaceIds")
            .Get<string[]>();

        if (allowlist is { Length: > 0 })
        {
            if (!allowlist.Contains(workspaceId, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[WorkspaceResolver] Workspace for tenant {TenantId} is not in the platform allowlist",
                    tenantId);
                return new WorkspaceResolutionResult(false, null, "workspace_not_allowlisted");
            }
        }

        return new WorkspaceResolutionResult(true, workspaceId, null);
    }

    /// <inheritdoc />
    public async ValueTask<WorkspaceResolutionResult> ResolveAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        var baseResult = Resolve(tenantId);

        if (_resourceDiscovery is null)
            return baseResult;

        // Check for explicitly configured App Insights resource path (takes priority over discovery).
        var configuredAiPath =
            _configuration[$"Tenants:{tenantId}:Observability:AppInsightsResourcePath"]
            ?? _configuration["Observability:AppInsightsResourcePath"];

        if (baseResult.Success && !string.IsNullOrWhiteSpace(configuredAiPath))
            return baseResult with { AppInsightsResourcePath = configuredAiPath };

        try
        {
            var cache = await GetCacheAsync(ct);

            if (!baseResult.Success)
            {
                // No workspace in config — attempt full auto-select from the in-memory index.
                return TryAutoSelectWorkspace(cache, tenantId) ?? baseResult;
            }

            // Workspace configured — O(1) lookup for linked App Insights component.
            if (cache.ByWorkspace.TryGetValue(baseResult.WorkspaceId!, out var matched)
                && !string.IsNullOrWhiteSpace(matched.AppInsightsResourcePath))
                return baseResult with { AppInsightsResourcePath = matched.AppInsightsResourcePath };

            return baseResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[WorkspaceResolver] Resource discovery failed for tenant {TenantId}; proceeding without App Insights filter",
                tenantId);
            return baseResult;
        }
    }

    private WorkspaceResolutionResult? TryAutoSelectWorkspace(
        WorkspaceDiscoveryCache cache,
        string tenantId)
    {
        // ── Narrow by subscription when tenant has one explicitly configured ─────
        // Dict<subscriptionId, [deduped workspaces]> makes this an O(1) lookup.
        var subscriptionId = _configuration[$"Tenants:{tenantId}:SubscriptionId"];

        IReadOnlyList<ObservabilityResourcePair> candidates;
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            if (!cache.BySubscription.TryGetValue(subscriptionId, out var subWorkspaces))
            {
                _logger.LogWarning(
                    "[WorkspaceResolver] No discovered workspace matches subscription {SubscriptionId} for tenant {TenantId}",
                    subscriptionId, tenantId);
                return null;
            }
            candidates = subWorkspaces;
        }
        else
        {
            // No subscription hint — use globally deduped workspace list.
            candidates = cache.AllWorkspaces;
        }

        if (candidates.Count > 1)
        {
            _logger.LogWarning(
                "[WorkspaceResolver] {Count} workspaces discovered for tenant {TenantId}; cannot auto-select — configure Tenants:{TenantId}:SubscriptionId to disambiguate",
                candidates.Count, tenantId, tenantId);
            return null;
        }

        var pair = candidates[0];
        var allowlist = _configuration
            .GetSection("SafeActions:AllowedLogAnalyticsWorkspaceIds")
            .Get<string[]>();

        if (allowlist is { Length: > 0 } &&
            !allowlist.Contains(pair.WorkspaceCustomerId, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[WorkspaceResolver] Auto-discovered workspace for tenant {TenantId} is not in the platform allowlist",
                tenantId);
            return new WorkspaceResolutionResult(false, null, "workspace_not_allowlisted");
        }

        _logger.LogInformation(
            "[WorkspaceResolver] Auto-discovered workspace {WorkspaceId} for tenant {TenantId} via ARM discovery",
            pair.WorkspaceCustomerId, tenantId);

        return new WorkspaceResolutionResult(true, pair.WorkspaceCustomerId, null, pair.AppInsightsResourcePath);
    }

    private async ValueTask<WorkspaceDiscoveryCache> GetCacheAsync(CancellationToken ct)
    {
        var cached = _cache;
        if (cached is not null)
            return cached;

        var pairs = await _resourceDiscovery!.DiscoverAsync(ct);

        // Index 1: subscriptionId → deduped workspaces in that subscription.
        var bySubscription = pairs
            .GroupBy(p => p.SubscriptionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ObservabilityResourcePair>)g
                    .GroupBy(p => p.WorkspaceCustomerId, StringComparer.OrdinalIgnoreCase)
                    .Select(wg => wg.First())
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        // Index 2: workspaceCustomerId → single pair (O(1) AI-path lookup).
        var byWorkspace = pairs
            .GroupBy(p => p.WorkspaceCustomerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        cached = new WorkspaceDiscoveryCache(bySubscription, byWorkspace);
        _cache = cached; // volatile write — benign race on cold start
        return cached;
    }

    /// <summary>In-memory indices built once from ARG discovery results.</summary>
    private sealed record WorkspaceDiscoveryCache(
        IReadOnlyDictionary<string, IReadOnlyList<ObservabilityResourcePair>> BySubscription,
        IReadOnlyDictionary<string, ObservabilityResourcePair>                ByWorkspace)
    {
        /// <summary>All distinct workspaces across all subscriptions (globally deduped by customer ID).</summary>
        public IReadOnlyList<ObservabilityResourcePair> AllWorkspaces { get; } =
            ByWorkspace.Values.ToList();
    }
}
