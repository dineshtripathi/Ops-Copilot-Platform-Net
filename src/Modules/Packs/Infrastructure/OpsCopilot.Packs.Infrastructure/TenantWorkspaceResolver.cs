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
            var pairs = await _resourceDiscovery.DiscoverAsync(ct);

            if (!baseResult.Success)
            {
                // No workspace in config — attempt full auto-select from discovery.
                return TryAutoSelectWorkspace(pairs, tenantId) ?? baseResult;
            }

            // Workspace configured — find the linked App Insights component.
            var matches = pairs
                .Where(p => string.Equals(p.WorkspaceCustomerId, baseResult.WorkspaceId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return baseResult;

            if (matches.Count > 1)
                _logger.LogWarning(
                    "[WorkspaceResolver] Multiple AI components linked to workspace {WorkspaceId}; using first match",
                    baseResult.WorkspaceId);

            return baseResult with { AppInsightsResourcePath = matches[0].AppInsightsResourcePath };
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
        IReadOnlyList<ObservabilityResourcePair> pairs,
        string tenantId)
    {
        if (pairs.Count == 0)
            return null;

        if (pairs.Count > 1)
        {
            _logger.LogWarning(
                "[WorkspaceResolver] Multiple LAW/AI pairs discovered for tenant {TenantId} with no workspace configured; cannot auto-select",
                tenantId);
            return null;
        }

        var pair = pairs[0];
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
}
