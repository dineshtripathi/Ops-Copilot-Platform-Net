using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Reads Azure resource metadata via the <see cref="ArmClient"/> SDK.
/// <para>
/// Uses <see cref="GenericResource.GetAsync"/> — this is a read-only GET call
/// against ARM that retrieves metadata for any resource type without requiring
/// resource-type-specific SDK packages.
/// </para>
/// This class NEVER performs writes, deletes, or POST actions.
/// </summary>
internal sealed class ArmResourceReader : IAzureResourceReader
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmResourceReader> _logger;

    public ArmResourceReader(ArmClient armClient, ILogger<ArmResourceReader> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    public async Task<AzureResourceMetadata> GetResourceMetadataAsync(
        string resourceId, CancellationToken ct)
    {
        _logger.LogInformation("[ArmResourceReader] GET metadata for {ResourceId}", resourceId);

        var identifier = new ResourceIdentifier(resourceId);
        var resource = _armClient.GetGenericResource(identifier);
        var response = await resource.GetAsync(ct);
        var data = response.Value.Data;

        _logger.LogInformation(
            "[ArmResourceReader] Retrieved {ResourceType} '{Name}' in {Location}",
            data.ResourceType.ToString(), data.Name, data.Location.Name);

        return new AzureResourceMetadata(
            Name: data.Name,
            ResourceType: data.ResourceType.ToString(),
            Location: data.Location.Name,
            ProvisioningState: data.ProvisioningState,
            Etag: response.GetRawResponse().Headers.ETag?.ToString(),
            TagsCount: data.Tags?.Count ?? 0);
    }
}
