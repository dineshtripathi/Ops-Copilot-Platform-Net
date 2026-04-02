using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Executes an ARM scale operation (GET capacity, then PATCH new capacity)
/// on a <c>Microsoft.Compute/virtualMachineScaleSets</c> resource.
/// <para>
/// Safety guardrails (evaluated in order — first failure wins):
/// <list type="bullet">
///   <item>Feature-gated via <c>SafeActions:EnableArmWrite=true</c> — disabled by default.</item>
///   <item>Payload must contain a valid, non-empty <c>resourceId</c> and non-negative <c>targetCapacity</c>.</item>
///   <item>Resource ID must start with <c>/subscriptions/</c> (ARM canonical form).</item>
///   <item>Resource ID must reference a <c>Microsoft.Compute/virtualMachineScaleSets</c> resource.</item>
///   <item>Subscription allowlist (<c>SafeActions:AllowedAzureSubscriptionIds</c>) — empty = allow all.</item>
///   <item><c>targetCapacity</c> must not exceed <c>SafeActions:MaxArmScaleCapacity</c> (default 100).</item>
///   <item>Configurable timeout (<c>SafeActions:ArmWriteTimeoutMs</c>, default 30 000 ms).</item>
/// </list>
/// </para>
/// Rollback is not supported — scaling cannot be automatically reverted.
/// </summary>
internal sealed class ArmScaleActionExecutor
{
    private const string VmssResourceTypeSegment =
        "/providers/Microsoft.Compute/virtualMachineScaleSets/";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IAzureScaleWriter _writer;
    private readonly ILogger<ArmScaleActionExecutor> _logger;
    private readonly bool _enableArmWrite;
    private readonly int _timeoutMs;
    private readonly int _maxCapacity;
    private readonly HashSet<string> _allowedSubscriptionIds;

    public ArmScaleActionExecutor(
        IAzureScaleWriter writer,
        IConfiguration configuration,
        ILogger<ArmScaleActionExecutor> logger)
    {
        _writer = writer;
        _logger = logger;
        _enableArmWrite = configuration.GetValue<bool>("SafeActions:EnableArmWrite");
        _timeoutMs = configuration.GetValue("SafeActions:ArmWriteTimeoutMs", 30_000);
        _maxCapacity = configuration.GetValue("SafeActions:MaxArmScaleCapacity", 100);

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

        // ── Feature gate ──────────────────────────────────────────────
        if (!_enableArmWrite)
        {
            return Fail("ARM_WRITE_DISABLED",
                "ARM write operations are disabled. Set SafeActions:EnableArmWrite=true to enable.",
                null, -1, sw);
        }

        // ── Parse payload ─────────────────────────────────────────────
        string? resourceId;
        int targetCapacity;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson, JsonOptions);
            var root = doc.RootElement;

            resourceId = root.TryGetProperty("resourceId", out var idProp)
                ? idProp.GetString()
                : null;

            targetCapacity = root.TryGetProperty("targetCapacity", out var capProp) &&
                             capProp.TryGetInt32(out var cap)
                ? cap
                : -1;
        }
        catch (JsonException)
        {
            return Fail("invalid_json", "payload is not valid JSON", null, -1, sw);
        }

        // ── Validate resource ID ──────────────────────────────────────
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Fail("invalid_payload",
                "payload must contain a non-empty 'resourceId' field", null, -1, sw);
        }

        if (!resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("invalid_resource_id",
                "resourceId must be a fully-qualified ARM resource ID starting with /subscriptions/",
                resourceId, -1, sw);
        }

        if (!resourceId.Contains(VmssResourceTypeSegment, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("invalid_resource_type",
                "resourceId must reference a Microsoft.Compute/virtualMachineScaleSets resource",
                resourceId, -1, sw);
        }

        // ── Subscription allowlist ────────────────────────────────────
        if (_allowedSubscriptionIds.Count > 0)
        {
            var segments = resourceId.Split('/');
            var subscriptionId = segments.Length > 2 ? segments[2] : "";

            if (!_allowedSubscriptionIds.Contains(subscriptionId))
            {
                return Fail("target_not_allowlisted",
                    $"Subscription '{subscriptionId}' is not in the allowed list",
                    resourceId, -1, sw);
            }
        }

        // ── Validate target capacity ──────────────────────────────────
        if (targetCapacity < 0)
        {
            return Fail("invalid_payload",
                "payload must contain a non-negative integer 'targetCapacity' field",
                resourceId, -1, sw);
        }

        if (targetCapacity > _maxCapacity)
        {
            return Fail("capacity_exceeded",
                $"targetCapacity {targetCapacity} exceeds maximum allowed ({_maxCapacity}). " +
                $"Adjust SafeActions:MaxArmScaleCapacity to increase the limit.",
                resourceId, targetCapacity, sw);
        }

        // ── Execute ARM scale ─────────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[ArmScale] Reading current capacity of {ResourceId}", resourceId);

            var previousCapacity = await _writer
                .GetCapacityAsync(resourceId, cts.Token)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[ArmScale] Scaling {ResourceId}: {Previous} → {Target} (timeout={TimeoutMs}ms)",
                resourceId, previousCapacity, targetCapacity, _timeoutMs);

            await _writer
                .SetCapacityAsync(resourceId, targetCapacity, cts.Token)
                .ConfigureAwait(false);

            sw.Stop();
            _logger.LogInformation(
                "[ArmScale] Scale accepted: {ResourceId} {Previous}→{Target} ({DurationMs}ms)",
                resourceId, previousCapacity, targetCapacity, sw.ElapsedMilliseconds);

            return new ActionExecutionResult(
                Success: true,
                ResponseJson: JsonSerializer.Serialize(new
                {
                    mode = "arm_scale",
                    resourceId,
                    previousCapacity,
                    targetCapacity,
                    durationMs = sw.ElapsedMilliseconds
                }),
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timeout",
                $"ARM scale operation timed out after {_timeoutMs}ms",
                resourceId, targetCapacity, sw);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[ArmScale] ARM REST call failed for {ResourceId}: {Message}",
                resourceId, ex.Message);
            return Fail("arm_api_error",
                $"ARM REST API call failed: {ex.Message}",
                resourceId, targetCapacity, sw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ArmScale] Unexpected error scaling {ResourceId}", resourceId);
            return Fail("unexpected_error",
                $"Unexpected error: {ex.GetType().Name}",
                resourceId, targetCapacity, sw);
        }
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(Fail("ROLLBACK_NOT_SUPPORTED",
            "Rollback is not supported for scale operations — the previous capacity is not stored.",
            null, -1, Stopwatch.StartNew()));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ActionExecutionResult Fail(
        string reason, string detail, string? resourceId, int targetCapacity,
        Stopwatch sw)
    {
        sw.Stop();
        return new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "arm_scale",
                reason,
                detail,
                resourceId,
                targetCapacity,
                durationMs = sw.ElapsedMilliseconds
            }),
            DurationMs: sw.ElapsedMilliseconds);
    }
}
