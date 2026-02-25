using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Executes a read-only Azure resource metadata GET via the ARM SDK.
/// <para>
/// Safety controls:
/// <list type="bullet">
///   <item>Read-only — only <c>GenericResource.GetAsync</c> (ARM GET) is used, never writes/deletes/POST</item>
///   <item>No custom token/header passthrough from payload — uses <c>DefaultAzureCredential</c></item>
///   <item>Resource ID format validated before making SDK call</item>
///   <item>Configurable timeout (<c>SafeActions:AzureReadTimeoutMs</c>, default 5 000 ms)</item>
///   <item>Subscription ID allowlist (<c>SafeActions:AllowedAzureSubscriptionIds</c>) — empty = allow all</item>
///   <item>Feature-gated via <c>SafeActions:EnableAzureReadExecutions</c> (default false)</item>
/// </list>
/// </para>
/// Rollback is not supported for Azure resource GET — returns a failure result.
/// </summary>
internal sealed class AzureResourceGetActionExecutor
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IAzureResourceReader _reader;
    private readonly ILogger<AzureResourceGetActionExecutor> _logger;
    private readonly int _timeoutMs;
    private readonly HashSet<string> _allowedSubscriptionIds;

    public AzureResourceGetActionExecutor(
        IAzureResourceReader reader,
        IConfiguration configuration,
        ILogger<AzureResourceGetActionExecutor> logger)
    {
        _reader = reader;
        _logger = logger;
        _timeoutMs = configuration.GetValue("SafeActions:AzureReadTimeoutMs", 5000);

        var raw = configuration.GetSection("SafeActions:AllowedAzureSubscriptionIds")
            .Get<string[]>() ?? [];
        _allowedSubscriptionIds = new HashSet<string>(
            raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        string payloadJson, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Parse payload ────────────────────────────────────────────
        string? resourceId;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson, JsonOptions);
            var root = doc.RootElement;

            resourceId = root.TryGetProperty("resourceId", out var idProp)
                ? idProp.GetString()
                : null;
        }
        catch (JsonException)
        {
            return Fail("invalid_json", "payload is not valid JSON", null, sw);
        }

        // ── Validate resource ID ─────────────────────────────────────
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Fail("invalid_payload",
                "payload must contain a non-empty 'resourceId' field", null, sw);
        }

        if (!resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("invalid_resource_id",
                "resourceId must be a fully-qualified ARM resource ID starting with /subscriptions/",
                resourceId, sw);
        }

        // ── Subscription allowlist ───────────────────────────────────
        if (_allowedSubscriptionIds.Count > 0)
        {
            var segments = resourceId.Split('/');
            var subscriptionId = segments.Length > 2 ? segments[2] : "";

            if (!_allowedSubscriptionIds.Contains(subscriptionId))
            {
                return Fail("target_not_allowlisted",
                    $"Subscription '{subscriptionId}' is not in the allowed list",
                    resourceId, sw);
            }
        }

        // ── Execute Azure GET ────────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[AzureResourceGet] GET metadata for {ResourceId} (timeout={TimeoutMs}ms)",
                resourceId, _timeoutMs);

            var metadata = await _reader.GetResourceMetadataAsync(resourceId, cts.Token);

            sw.Stop();

            _logger.LogInformation(
                "[AzureResourceGet] {ResourceType} '{Name}' in {Location} ({DurationMs}ms)",
                metadata.ResourceType, metadata.Name, metadata.Location,
                sw.ElapsedMilliseconds);

            var responseJson = JsonSerializer.Serialize(new
            {
                mode = "azure_resource_get",
                resourceId,
                name = metadata.Name,
                resourceType = metadata.ResourceType,
                location = metadata.Location,
                provisioningState = metadata.ProvisioningState,
                etag = metadata.Etag,
                tagsCount = metadata.TagsCount,
                durationMs = sw.ElapsedMilliseconds
            });

            return new ActionExecutionResult(
                Success: true,
                ResponseJson: responseJson,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogWarning(ex,
                "[AzureResourceGet] Authentication failed for {ResourceId}", resourceId);
            return Fail("azure_auth_failed", ex.Message, resourceId, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogWarning(ex,
                "[AzureResourceGet] Forbidden for {ResourceId}", resourceId);
            return Fail("azure_forbidden", ex.Message, resourceId, sw);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex,
                "[AzureResourceGet] Not found: {ResourceId}", resourceId);
            return Fail("azure_not_found", ex.Message, resourceId, sw);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "[AzureResourceGet] ARM request failed for {ResourceId}", resourceId);
            return Fail("azure_request_failed", ex.Message, resourceId, sw);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[AzureResourceGet] Timeout after {TimeoutMs}ms for {ResourceId}",
                _timeoutMs, resourceId);
            return Fail("azure_timeout",
                $"request timed out after {_timeoutMs}ms", resourceId, sw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AzureResourceGet] Unexpected error for {ResourceId}", resourceId);
            return Fail("unexpected_error", ex.Message, resourceId, sw);
        }
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "azure_resource_get",
                reason = "rollback is not supported for azure_resource_get"
            }),
            DurationMs: 0));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ActionExecutionResult Fail(
        string reason, string detail, string? resourceId, Stopwatch sw)
    {
        sw.Stop();
        return new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "azure_resource_get",
                reason,
                detail,
                resourceId = resourceId ?? "",
                durationMs = sw.ElapsedMilliseconds
            }),
            DurationMs: sw.ElapsedMilliseconds);
    }
}
