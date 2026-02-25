namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Abstraction for reading Azure resource metadata via ARM.
/// Enables unit testing of <see cref="AzureResourceGetActionExecutor"/>
/// without live Azure credentials.
/// </summary>
internal interface IAzureResourceReader
{
    /// <summary>
    /// Retrieves read-only metadata for the specified ARM resource.
    /// </summary>
    /// <param name="resourceId">Fully qualified ARM resource ID
    /// (e.g. <c>/subscriptions/{sub}/resourceGroups/{rg}/providers/{ns}/{type}/{name}</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Metadata for the resource.</returns>
    Task<AzureResourceMetadata> GetResourceMetadataAsync(string resourceId, CancellationToken ct);
}

/// <summary>
/// Read-only metadata returned by <see cref="IAzureResourceReader"/>.
/// Contains only safe, non-secret fields.
/// </summary>
/// <param name="Name">Resource display name.</param>
/// <param name="ResourceType">ARM resource type (e.g. <c>Microsoft.Compute/virtualMachines</c>).</param>
/// <param name="Location">Azure region.</param>
/// <param name="ProvisioningState">Current provisioning state.</param>
/// <param name="Etag">Resource ETag (may be null).</param>
/// <param name="TagsCount">Number of tags on the resource.</param>
internal sealed record AzureResourceMetadata(
    string Name,
    string ResourceType,
    string Location,
    string? ProvisioningState,
    string? Etag,
    int TagsCount);
