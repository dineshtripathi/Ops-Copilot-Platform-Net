namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// A correlated pair of an Azure Log Analytics workspace (identified by its customerId GUID)
/// and an optionally linked Application Insights component.
/// </summary>
/// <param name="WorkspaceCustomerId">
/// The Log Analytics workspace customer ID (GUID string) — used as the workspaceId in KQL queries.
/// </param>
/// <param name="AppInsightsName">Friendly name of the App Insights component, or empty when no component is linked.</param>
/// <param name="AppInsightsResourcePath">
/// Lowercase ARM resource path suffix used as a KQL <c>_ResourceId has "…"</c> filter,
/// e.g. <c>/providers/microsoft.insights/components/rnli-bfingress-dev-ai</c>.
/// Empty when no App Insights component is linked to the workspace.
/// </param>
public sealed record ObservabilityResourcePair(
    string WorkspaceCustomerId,
    string AppInsightsName,
    string AppInsightsResourcePath)
{
    /// <summary>
    /// The Azure subscription ID that owns this Log Analytics workspace.
    /// Used to narrow auto-discovery when a tenant maps to a specific subscription.
    /// </summary>
    public string SubscriptionId { get; init; } = string.Empty;
}

/// <summary>
/// Discovers correlations between Log Analytics workspaces and linked App Insights components
/// across all accessible Azure subscriptions.
/// </summary>
public interface IObservabilityResourceDiscovery
{
    /// <summary>
    /// Returns all discovered workspace / App Insights pairs.
    /// Results may be cached by the implementation.
    /// Returns an empty list when discovery is unavailable or fails.
    /// </summary>
    Task<IReadOnlyList<ObservabilityResourcePair>> DiscoverAsync(CancellationToken ct = default);
}
