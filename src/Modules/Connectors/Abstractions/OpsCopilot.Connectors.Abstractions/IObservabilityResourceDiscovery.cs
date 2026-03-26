namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// A correlated pair of an Azure Log Analytics workspace (identified by its customerId GUID)
/// and a linked Application Insights component.
/// </summary>
/// <param name="WorkspaceCustomerId">
/// The Log Analytics workspace customer ID (GUID string) — used as the workspaceId in KQL queries.
/// </param>
/// <param name="AppInsightsName">Friendly name of the App Insights component (e.g. <c>rnli-bfingress-dev-ai</c>).</param>
/// <param name="AppInsightsResourcePath">
/// Lowercase ARM resource path suffix used as a KQL <c>_ResourceId has "…"</c> filter,
/// e.g. <c>/providers/microsoft.insights/components/rnli-bfingress-dev-ai</c>.
/// </param>
public sealed record ObservabilityResourcePair(
    string WorkspaceCustomerId,
    string AppInsightsName,
    string AppInsightsResourcePath);

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
