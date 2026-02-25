using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Validators;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Real HTTP probe executor — performs an outbound HTTPS GET to a validated target
/// and returns the status code + truncated response body.
/// <para>
/// Safety controls:
/// <list type="bullet">
///   <item>SSRF protection via <see cref="TargetUriValidator"/></item>
///   <item>HTTPS only, GET only — no request body, no auth headers</item>
///   <item>Configurable timeout (<c>SafeActions:HttpProbeTimeoutMs</c>, default 5 000 ms)</item>
///   <item>Response body capped (<c>SafeActions:HttpProbeMaxResponseBytes</c>, default 1 024)</item>
/// </list>
/// </para>
/// Rollback is not supported for HTTP probes — returns a failure result.
/// </summary>
internal sealed class HttpProbeActionExecutor
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly TargetUriValidator _validator;
    private readonly ILogger<HttpProbeActionExecutor> _logger;
    private readonly int _timeoutMs;
    private readonly int _maxResponseBytes;

    public HttpProbeActionExecutor(
        HttpClient httpClient,
        TargetUriValidator validator,
        IConfiguration configuration,
        ILogger<HttpProbeActionExecutor> logger)
    {
        _httpClient = httpClient;
        _validator = validator;
        _logger = logger;
        _timeoutMs = configuration.GetValue("SafeActions:HttpProbeTimeoutMs", 5000);
        _maxResponseBytes = configuration.GetValue("SafeActions:HttpProbeMaxResponseBytes", 1024);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        string payloadJson, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Parse payload ────────────────────────────────────────────
        string? url;
        string? method;
        int? perRequestTimeoutMs;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson, JsonOptions);
            var root = doc.RootElement;

            url = root.TryGetProperty("url", out var urlProp)
                ? urlProp.GetString()
                : null;

            method = root.TryGetProperty("method", out var methodProp)
                ? methodProp.GetString()
                : "GET";

            perRequestTimeoutMs = root.TryGetProperty("timeoutMs", out var toProp)
                && toProp.TryGetInt32(out var toVal)
                ? toVal
                : null;
        }
        catch (JsonException)
        {
            return Fail("invalid_json", "payload is not valid JSON", null, sw);
        }

        // ── Validate method ──────────────────────────────────────────
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("method_not_allowed",
                $"only GET is allowed; got {method}", url, sw);
        }

        // ── Validate URL ─────────────────────────────────────────────
        var (isValid, reason) = _validator.Validate(url);
        if (!isValid)
        {
            _logger.LogWarning(
                "[HttpProbe] URL validation failed: {Reason}", reason);
            return Fail("url_blocked", reason!, url, sw);
        }

        // ── Execute HTTP GET ─────────────────────────────────────────
        var effectiveTimeout = perRequestTimeoutMs ?? _timeoutMs;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            _logger.LogInformation(
                "[HttpProbe] GET {Url} (timeout={TimeoutMs}ms)", url, effectiveTimeout);

            using var response = await _httpClient.GetAsync(url!, cts.Token);
            var body = await ReadCappedBodyAsync(response, cts.Token);
            var truncated = body.Length >= _maxResponseBytes;

            sw.Stop();

            _logger.LogInformation(
                "[HttpProbe] {Url} → {StatusCode} ({DurationMs}ms, {BodyLen} bytes, truncated={Truncated})",
                url, (int)response.StatusCode, sw.ElapsedMilliseconds, body.Length, truncated);

            var responseJson = JsonSerializer.Serialize(new
            {
                mode = "real_http_probe",
                statusCode = (int)response.StatusCode,
                url,
                truncated,
                responseSnippet = body,
                durationMs = sw.ElapsedMilliseconds
            });

            return new ActionExecutionResult(
                Success: response.IsSuccessStatusCode,
                ResponseJson: responseJson,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[HttpProbe] Timeout after {TimeoutMs}ms for {Url}", effectiveTimeout, url);
            return Fail("timeout",
                $"request timed out after {effectiveTimeout}ms", url, sw);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "[HttpProbe] HTTP request failed for {Url}", url);
            return Fail("http_error", ex.Message, url, sw);
        }
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "real_http_probe",
                reason = "rollback is not supported for http_probe"
            }),
            DurationMs: 0));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<string> ReadCappedBodyAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[_maxResponseBytes];
        var totalRead = 0;
        int bytesRead;
        while (totalRead < _maxResponseBytes &&
               (bytesRead = await stream.ReadAsync(
                   buffer.AsMemory(totalRead, _maxResponseBytes - totalRead), ct)) > 0)
        {
            totalRead += bytesRead;
        }
        return System.Text.Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    private static ActionExecutionResult Fail(
        string reason, string detail, string? url, Stopwatch sw)
    {
        sw.Stop();
        return new ActionExecutionResult(
            Success: false,
            ResponseJson: JsonSerializer.Serialize(new
            {
                mode = "real_http_probe",
                reason,
                detail,
                url = url ?? "",
                durationMs = sw.ElapsedMilliseconds
            }),
            DurationMs: sw.ElapsedMilliseconds);
    }
}
