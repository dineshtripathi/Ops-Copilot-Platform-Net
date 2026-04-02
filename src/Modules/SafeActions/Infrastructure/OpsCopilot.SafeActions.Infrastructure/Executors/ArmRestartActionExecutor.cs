using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Executes an ARM VM restart via the Azure Resource Manager REST API.
/// <para>
/// Safety guardrails (evaluated in order — first failure wins):
/// <list type="bullet">
///   <item>Feature-gated via <c>SafeActions:EnableArmWrite=true</c> — disabled by default.</item>
///   <item>Payload must contain a valid, non-empty <c>resourceId</c> field.</item>
///   <item>Resource ID must start with <c>/subscriptions/</c> (ARM canonical form).</item>
///   <item>Resource ID must reference a <c>Microsoft.Compute/virtualMachines</c> resource.</item>
///   <item>Subscription allowlist (<c>SafeActions:AllowedAzureSubscriptionIds</c>) — empty = allow all.</item>
///   <item>Configurable timeout (<c>SafeActions:ArmWriteTimeoutMs</c>, default 30 000 ms).</item>
///   <item>Uses <see cref="IAzureVmWriter"/> abstraction — credentials never in this class.</item>
/// </list>
/// </para>
/// Rollback is not supported — a VM restart cannot be undone.
/// </summary>
internal sealed class ArmRestartActionExecutor
{
    private const string VmResourceTypeSegment =
        "/providers/Microsoft.Compute/virtualMachines/";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IAzureVmWriter _writer;
    private readonly ILogger<ArmRestartActionExecutor> _logger;
    private readonly bool _enableArmWrite;
    private readonly int _timeoutMs;
    private readonly HashSet<string> _allowedSubscriptionIds;

    public ArmRestartActionExecutor(
        IAzureVmWriter writer,
        IConfiguration configuration,
        ILogger<ArmRestartActionExecutor> logger)
    {
        _writer = writer;
        _logger = logger;
        _enableArmWrite = configuration.GetValue<bool>("SafeActions:EnableArmWrite");
        _timeoutMs = configuration.GetValue("SafeActions:ArmWriteTimeoutMs", 30_000);

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
                null, sw);
        }

        // ── Parse payload ─────────────────────────────────────────────
        string? resourceId;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson, JsonOptions);
            resourceId = doc.RootElement.TryGetProperty("resourceId", out var prop)
                ? prop.GetString()
                : null;
        }
        catch (JsonException)
        {
            return Fail("invalid_json", "payload is not valid JSON", null, sw);
        }

        // ── Validate resource ID ──────────────────────────────────────
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

        if (!resourceId.Contains(VmResourceTypeSegment, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("invalid_resource_type",
                "resourceId must reference a Microsoft.Compute/virtualMachines resource",
                resourceId, sw);
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
                    resourceId, sw);
            }
        }

        // ── Execute ARM restart ───────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[ArmRestart] Restarting VM {ResourceId} (timeout={TimeoutMs}ms)",
                resourceId, _timeoutMs);

            await _writer.RestartAsync(resourceId, cts.Token).ConfigureAwait(false);

            sw.Stop();
            _logger.LogInformation(
                "[ArmRestart] Restart accepted for {ResourceId} ({DurationMs}ms)",
                resourceId, sw.ElapsedMilliseconds);

            return new ActionExecutionResult(
                Success: true,
                ResponseJson: JsonSerializer.Serialize(new
                {
                    mode = "arm_restart",
                    resourceId,
                    durationMs = sw.ElapsedMilliseconds
                }),
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timeout",
                $"ARM restart timed out after {_timeoutMs}ms", resourceId, sw);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[ArmRestart] ARM REST call failed for {ResourceId}: {Message}",
                resourceId, ex.Message);
            return Fail("arm_api_error",
                $"ARM REST API call failed: {ex.Message}", resourceId, sw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ArmRestart] Unexpected error restarting {ResourceId}", resourceId);
            return Fail("unexpected_error",
                $"Unexpected error: {ex.GetType().Name}", resourceId, sw);
        }
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(Fail("ROLLBACK_NOT_SUPPORTED",
            "Rollback is not supported for VM restart — the operation cannot be undone.",
            null, Stopwatch.StartNew()));
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
                mode = "arm_restart",
                reason,
                detail,
                resourceId = resourceId ?? "",
                durationMs = sw.ElapsedMilliseconds
            }),
            DurationMs: sw.ElapsedMilliseconds);
    }
}
