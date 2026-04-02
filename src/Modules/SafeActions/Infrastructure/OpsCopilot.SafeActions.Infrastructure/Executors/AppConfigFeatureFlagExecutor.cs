using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Executes an Azure App Configuration feature flag toggle (GET current state,
/// then PUT new enabled state).
/// <para>
/// Safety guardrails (evaluated in order — first failure wins):
/// <list type="bullet">
///   <item>Feature-gated via <c>SafeActions:EnableAppConfigWrite=true</c> — disabled by default.</item>
///   <item>Payload must contain a valid, non-empty <c>endpoint</c> and <c>featureFlagId</c>.</item>
///   <item>Endpoint must start with <c>https://</c> and end with <c>.azconfig.io</c>.</item>
///   <item>Payload must contain an explicit boolean <c>enabled</c> field.</item>
///   <item>Endpoint allowlist (<c>SafeActions:AllowedAppConfigEndpoints</c>) — empty = allow all.</item>
///   <item>Configurable timeout (<c>SafeActions:AppConfigWriteTimeoutMs</c>, default 30 000 ms).</item>
/// </list>
/// </para>
/// Rollback is supported — the rollback payload should contain the desired prior
/// enabled state and will be applied verbatim.
/// </summary>
internal sealed class AppConfigFeatureFlagExecutor
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IAppConfigFeatureFlagWriter _writer;
    private readonly ILogger<AppConfigFeatureFlagExecutor> _logger;
    private readonly bool _enableAppConfigWrite;
    private readonly int _timeoutMs;
    private readonly HashSet<string> _allowedEndpoints;

    public AppConfigFeatureFlagExecutor(
        IAppConfigFeatureFlagWriter writer,
        IConfiguration configuration,
        ILogger<AppConfigFeatureFlagExecutor> logger)
    {
        _writer = writer;
        _logger = logger;
        _enableAppConfigWrite = configuration
            .GetValue<bool>("SafeActions:EnableAppConfigWrite");
        _timeoutMs = configuration
            .GetValue("SafeActions:AppConfigWriteTimeoutMs", 30_000);

        var raw = configuration
            .GetSection("SafeActions:AllowedAppConfigEndpoints")
            .Get<string[]>() ?? [];
        _allowedEndpoints = new HashSet<string>(
            raw.Where(s => !string.IsNullOrWhiteSpace(s))
               .Select(s => s.Trim().TrimEnd('/').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        string payloadJson, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Feature gate ──────────────────────────────────────────────
        if (!_enableAppConfigWrite)
        {
            return Fail("APP_CONFIG_WRITE_DISABLED",
                "App Configuration write operations are disabled. " +
                "Set SafeActions:EnableAppConfigWrite=true to enable.",
                null, null, sw);
        }

        // ── Parse and validate payload ────────────────────────────────
        if (!TryParsePayload(payloadJson, out var endpoint, out var featureFlagId,
                out var enabled, out var parseReason, out var parseDetail))
        {
            return Fail(parseReason!, parseDetail!, null, null, sw);
        }

        // ── Endpoint allowlist ────────────────────────────────────────
        var normalizedEndpoint = endpoint!.TrimEnd('/').ToLowerInvariant();
        if (_allowedEndpoints.Count > 0 && !_allowedEndpoints.Contains(normalizedEndpoint))
        {
            return Fail("endpoint_not_allowlisted",
                $"Endpoint '{endpoint}' is not in the allowed list",
                endpoint, featureFlagId, sw);
        }

        // ── Execute ───────────────────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[AppConfigFf] Reading current state of '{FeatureFlagId}' from {Endpoint}",
                featureFlagId, endpoint);

            var previousEnabled = await _writer
                .GetEnabledAsync(endpoint!, featureFlagId!, cts.Token)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[AppConfigFf] Setting '{FeatureFlagId}' {Previous} → {New} at {Endpoint} (timeout={TimeoutMs}ms)",
                featureFlagId, previousEnabled, enabled, endpoint, _timeoutMs);

            await _writer
                .SetEnabledAsync(endpoint!, featureFlagId!, enabled!.Value, cts.Token)
                .ConfigureAwait(false);

            sw.Stop();
            _logger.LogInformation(
                "[AppConfigFf] Flag '{FeatureFlagId}' updated {Previous}→{New} at {Endpoint} ({DurationMs}ms)",
                featureFlagId, previousEnabled, enabled, endpoint, sw.ElapsedMilliseconds);

            return new ActionExecutionResult(
                Success: true,
                ResponseJson: JsonSerializer.Serialize(new
                {
                    mode = "app_config_feature_flag",
                    endpoint,
                    featureFlagId,
                    enabled,
                    previousEnabled,
                    durationMs = sw.ElapsedMilliseconds
                }),
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timeout",
                $"App Configuration operation timed out after {_timeoutMs}ms",
                endpoint, featureFlagId, sw);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[AppConfigFf] REST call failed for '{FeatureFlagId}' at {Endpoint}: {Message}",
                featureFlagId, endpoint, ex.Message);
            return Fail("app_config_api_error",
                $"App Configuration REST API call failed: {ex.Message}",
                endpoint, featureFlagId, sw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AppConfigFf] Unexpected error toggling '{FeatureFlagId}' at {Endpoint}",
                featureFlagId, endpoint);
            return Fail("unexpected_error",
                $"Unexpected error: {ex.GetType().Name}",
                endpoint, featureFlagId, sw);
        }
    }

    public async Task<ActionExecutionResult> RollbackAsync(
        string payloadJson, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Feature gate ──────────────────────────────────────────────
        if (!_enableAppConfigWrite)
        {
            return Fail("APP_CONFIG_WRITE_DISABLED",
                "App Configuration write operations are disabled. " +
                "Set SafeActions:EnableAppConfigWrite=true to enable.",
                null, null, sw);
        }

        // ── Parse rollback payload ────────────────────────────────────
        if (!TryParsePayload(payloadJson, out var endpoint, out var featureFlagId,
                out var enabled, out var parseReason, out var parseDetail))
        {
            return Fail(parseReason!, parseDetail!, null, null, sw);
        }

        // ── Apply rollback value ──────────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutMs);

        try
        {
            _logger.LogInformation(
                "[AppConfigFf] Rolling back '{FeatureFlagId}' to enabled={Enabled} at {Endpoint}",
                featureFlagId, enabled, endpoint);

            await _writer
                .SetEnabledAsync(endpoint!, featureFlagId!, enabled!.Value, cts.Token)
                .ConfigureAwait(false);

            sw.Stop();
            _logger.LogInformation(
                "[AppConfigFf] Rollback complete: '{FeatureFlagId}' set to {Enabled} at {Endpoint} ({DurationMs}ms)",
                featureFlagId, enabled, endpoint, sw.ElapsedMilliseconds);

            return new ActionExecutionResult(
                Success: true,
                ResponseJson: JsonSerializer.Serialize(new
                {
                    mode = "app_config_feature_flag_rollback",
                    endpoint,
                    featureFlagId,
                    enabled,
                    durationMs = sw.ElapsedMilliseconds
                }),
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timeout",
                $"App Configuration rollback timed out after {_timeoutMs}ms",
                endpoint, featureFlagId, sw);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[AppConfigFf] Rollback REST call failed for '{FeatureFlagId}' at {Endpoint}: {Message}",
                featureFlagId, endpoint, ex.Message);
            return Fail("app_config_api_error",
                $"App Configuration REST API call failed during rollback: {ex.Message}",
                endpoint, featureFlagId, sw);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static bool TryParsePayload(
        string payloadJson,
        out string? endpoint,
        out string? featureFlagId,
        out bool? enabled,
        out string? reason,
        out string? detail)
    {
        endpoint = null;
        featureFlagId = null;
        enabled = null;
        reason = null;
        detail = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            reason = "invalid_json";
            detail = "payload is not valid JSON";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;

            endpoint = root.TryGetProperty("endpoint", out var epProp)
                ? epProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                reason = "invalid_payload";
                detail = "payload must contain a non-empty 'endpoint' field";
                return false;
            }

            if (!endpoint!.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                !endpoint.EndsWith(".azconfig.io", StringComparison.OrdinalIgnoreCase))
            {
                reason = "invalid_endpoint";
                detail = "endpoint must start with 'https://' and end with '.azconfig.io'";
                return false;
            }

            featureFlagId = root.TryGetProperty("featureFlagId", out var idProp)
                ? idProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(featureFlagId))
            {
                reason = "invalid_payload";
                detail = "payload must contain a non-empty 'featureFlagId' field";
                return false;
            }

            if (!root.TryGetProperty("enabled", out var enabledProp) ||
                enabledProp.ValueKind != JsonValueKind.True &&
                enabledProp.ValueKind != JsonValueKind.False)
            {
                reason = "invalid_payload";
                detail = "payload must contain an explicit boolean 'enabled' field";
                return false;
            }

            enabled = enabledProp.GetBoolean();
        }

        return true;
    }

    private static ActionExecutionResult Fail(
        string reason, string detail, string? endpoint, string? featureFlagId, Stopwatch sw)
    {
        sw.Stop();
        return new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "app_config_feature_flag",
                reason,
                detail,
                endpoint,
                featureFlagId,
                durationMs = sw.ElapsedMilliseconds
            }),
            DurationMs: sw.ElapsedMilliseconds);
    }
}
