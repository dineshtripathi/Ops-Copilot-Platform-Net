using System.ComponentModel;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "arm_resource_get" MCP tool.
///
/// Fetches metadata for a single Azure resource by its fully qualified
/// ARM resource ID using the shared <see cref="ArmClient"/> singleton.
///
/// Used by SafeActions.Infrastructure to replace the direct Azure SDK
/// boundary violation in ArmResourceReader.
///
/// On success  → ok=true,  resource fields populated, error=null.
/// On failure  → ok=false, fields empty/null, error contains message.
/// </summary>
[McpServerToolType]
public sealed class ArmResourceGetTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    [McpServerTool(Name = "arm_resource_get")]
    [Description(
        "Fetches metadata for a single Azure resource by its fully qualified ARM resource ID. " +
        "Returns name, resourceType, location, provisioningState, etag, and tagCount. " +
        "On success returns ok=true. On failure returns ok=false with an error field.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton in Program.cs
        ArmClient        armClient,
        ILoggerFactory   loggerFactory,
        IHostEnvironment hostEnvironment,

        // MCP tool parameters
        [Description(
            "Fully qualified ARM resource ID, e.g. " +
            "'/subscriptions/{subId}/resourceGroups/{rg}/providers/{ns}/{type}/{name}'.")]
        string resourceId,

        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(ArmResourceGetTool));

        // ── Input validation ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Fail(resourceId, "resourceId must not be empty.", "ValidationError");
        }

        if (!resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(resourceId,
                $"resourceId '{resourceId}' does not look like a valid ARM resource ID.",
                "ValidationError");
        }

        // ── Fetch resource ────────────────────────────────────────────────────
        try
        {
            logger.LogDebug("arm_resource_get | resourceId={ResourceId}", resourceId);

            var identifier = new ResourceIdentifier(resourceId);
            var genericResource = armClient.GetGenericResource(identifier);
            var response = await genericResource.GetAsync(cancellationToken);
            var data = response.Value.Data;

            var result = new
            {
                ok              = true,
                resourceId,
                name            = data.Name ?? string.Empty,
                resourceType    = data.ResourceType.ToString(),
                location        = data.Location.Name,
                provisioningState = data.ProvisioningState?.ToString(),
                tagCount        = data.Tags?.Count ?? 0,
                error           = (string?)null,
            };

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "arm_resource_get failed | resourceId={ResourceId}", resourceId);
            return Fail(resourceId, ex.Message, ex.GetType().Name);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Fail(string resourceId, string message, string errorType)
    {
        var payload = new
        {
            ok              = false,
            resourceId,
            name            = string.Empty,
            resourceType    = string.Empty,
            location        = string.Empty,
            provisioningState = (string?)null,
            etag            = (string?)null,
            tagCount        = 0,
            error           = $"[{errorType}] {message}",
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }
}
