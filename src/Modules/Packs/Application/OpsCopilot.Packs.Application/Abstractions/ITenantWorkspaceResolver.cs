namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Result of resolving a Log Analytics workspace ID for a given tenant.
/// </summary>
/// <param name="Success">Whether a usable workspace was found.</param>
/// <param name="WorkspaceId">The resolved workspace GUID, or <c>null</c> on failure.</param>
/// <param name="ErrorCode">
/// Error code when <paramref name="Success"/> is <c>false</c>:
/// <list type="bullet">
///   <item><c>missing_workspace</c> — no workspace configured for the tenant, or the value is not a valid GUID.</item>
///   <item><c>workspace_not_allowlisted</c> — workspace GUID is valid but not in the platform allowlist.</item>
/// </list>
/// </param>
/// <param name="AppInsightsResourcePath">
/// Optional ARM resource path suffix for the App Insights component linked to this workspace,
/// e.g. <c>/providers/microsoft.insights/components/my-ai</c>.
/// When set, evidence executors inject a <c>| where _ResourceId has "..."</c> filter into
/// App Insights KQL queries to scope results to the specific component in a shared workspace.
/// <c>null</c> means no filter is applied.
/// </param>
public sealed record WorkspaceResolutionResult(
    bool    Success,
    string? WorkspaceId,
    string? ErrorCode,
    string? AppInsightsResourcePath = null);

/// <summary>
/// Resolves the Log Analytics workspace ID for a given tenant from tenancy-resolved
/// configuration (key <c>Observability:LogAnalyticsWorkspaceId</c>).
/// Used by evidence executors to determine the target workspace for KQL queries.
/// </summary>
public interface ITenantWorkspaceResolver
{
    /// <summary>
    /// Resolves the workspace for the specified tenant synchronously from configuration.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the request header.</param>
    /// <returns>
    /// <see cref="WorkspaceResolutionResult"/> with <see cref="WorkspaceResolutionResult.Success"/> set
    /// to <c>true</c> and a valid <see cref="WorkspaceResolutionResult.WorkspaceId"/>, or
    /// <c>false</c> with an <see cref="WorkspaceResolutionResult.ErrorCode"/>.
    /// </returns>
    WorkspaceResolutionResult Resolve(string tenantId);

    /// <summary>
    /// Resolves the workspace for the specified tenant, optionally enriching the result
    /// with an <see cref="WorkspaceResolutionResult.AppInsightsResourcePath"/> via ARM discovery.
    /// The default implementation delegates to <see cref="Resolve"/> with no enrichment.
    /// </summary>
    ValueTask<WorkspaceResolutionResult> ResolveAsync(
        string tenantId,
        CancellationToken ct = default)
        => ValueTask.FromResult(Resolve(tenantId));
}

