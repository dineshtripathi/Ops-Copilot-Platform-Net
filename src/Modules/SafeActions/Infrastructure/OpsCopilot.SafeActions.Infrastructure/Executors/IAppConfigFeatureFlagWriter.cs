namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Reads and writes Azure App Configuration feature flags via the REST API.
/// </summary>
internal interface IAppConfigFeatureFlagWriter
{
    /// <summary>Gets whether the named feature flag is currently enabled.</summary>
    Task<bool> GetEnabledAsync(string endpoint, string featureFlagId, CancellationToken ct);

    /// <summary>Sets the enabled state of the named feature flag.</summary>
    Task SetEnabledAsync(string endpoint, string featureFlagId, bool enabled, CancellationToken ct);
}
