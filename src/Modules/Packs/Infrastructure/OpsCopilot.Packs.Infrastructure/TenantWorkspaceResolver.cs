using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Infrastructure;

/// <summary>
/// Resolves the Log Analytics workspace ID for a tenant by reading from configuration.
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>Tenant-specific key: <c>Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId</c></item>
///   <item>Global fallback key: <c>Observability:LogAnalyticsWorkspaceId</c></item>
/// </list>
/// </para>
/// <para>
/// After resolving a GUID, the workspace is validated against
/// <c>SafeActions:AllowedLogAnalyticsWorkspaceIds</c> when that list is non-empty.
/// </para>
/// </summary>
internal sealed class TenantWorkspaceResolver : ITenantWorkspaceResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantWorkspaceResolver> _logger;

    public TenantWorkspaceResolver(
        IConfiguration configuration,
        ILogger<TenantWorkspaceResolver> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public WorkspaceResolutionResult Resolve(string tenantId)
    {
        // ── 1. Locate workspace ID in config ────────────────────────────────
        var workspaceId =
            _configuration[$"Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId"]
            ?? _configuration["Observability:LogAnalyticsWorkspaceId"];

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
}
