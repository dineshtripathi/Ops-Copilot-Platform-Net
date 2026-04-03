using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Infrastructure.McpClient;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// MCP-backed implementation of <see cref="IAzureResourceReader"/>.
///
/// Routes all Azure ARM calls through the OpsCopilot.McpHost child process
/// via the <c>arm_resource_get</c> MCP tool.
///
/// Boundary rule: this class MUST NOT reference Azure.ResourceManager.
/// </summary>
internal sealed class McpArmResourceReader : IAzureResourceReader
{
    private readonly SafeActionsMcpHostClient       _mcp;
    private readonly ILogger<McpArmResourceReader>  _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public McpArmResourceReader(
        SafeActionsMcpHostClient        mcp,
        ILogger<McpArmResourceReader>   logger)
    {
        _mcp    = mcp;
        _logger = logger;
    }

    public async Task<AzureResourceMetadata> GetResourceMetadataAsync(
        string            resourceId,
        CancellationToken ct)
    {
        var json = await _mcp.CallToolAsync(
            "arm_resource_get",
            new Dictionary<string, object?> { ["resourceId"] = resourceId },
            ct);

        var doc = JsonDocument.Parse(json).RootElement;

        if (!doc.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var err = doc.TryGetProperty("error", out var ep)
                ? (ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? json : ep.GetRawText())
                : json;
            _logger.LogWarning(
                "arm_resource_get returned ok=false for resource. error={Error}", err);
            throw new InvalidOperationException(
                $"arm_resource_get failed: {err}");
        }

        var name              = doc.TryGetProperty("name",              out var v) ? v.GetString() : null;
        var resourceType      = doc.TryGetProperty("resourceType",      out     v) ? v.GetString() : null;
        var location          = doc.TryGetProperty("location",          out     v) ? v.GetString() : null;
        var provisioningState = doc.TryGetProperty("provisioningState", out     v) ? v.GetString() : null;
        var etag              = doc.TryGetProperty("etag",              out     v) ? v.GetString() : null;
        var tagCount          = doc.TryGetProperty("tagCount",          out     v) ? v.GetInt32() : 0;

        return new AzureResourceMetadata(
            Name:              name              ?? string.Empty,
            ResourceType:      resourceType      ?? string.Empty,
            Location:          location          ?? string.Empty,
            ProvisioningState: provisioningState,
            Etag:              etag,
            TagsCount:         tagCount);
    }
}
