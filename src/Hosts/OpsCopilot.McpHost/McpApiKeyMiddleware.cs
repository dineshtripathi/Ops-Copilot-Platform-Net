using Microsoft.Extensions.Logging;

namespace OpsCopilot.McpHost;

/// <summary>
/// Middleware that validates inbound requests to the MCP SSE endpoint
/// against a configured API key. Accepts the key in either:
///   <c>Authorization: Bearer &lt;key&gt;</c> or
///   <c>X-Api-Key: &lt;key&gt;</c> header.
///
/// When <c>McpAuth:ApiKey</c> is empty or not configured the middleware
/// logs a warning at startup and allows all requests through (dev-safe fallback).
/// The key value is never logged.
/// </summary>
internal sealed class McpApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _configuredKey;
    private readonly ILogger<McpApiKeyMiddleware> _logger;

    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string AuthorizationHeaderName = "Authorization";
    private const string BearerPrefix = "Bearer ";

    public McpApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<McpApiKeyMiddleware> logger)
    {
        _next = next;
        _configuredKey = configuration["McpAuth:ApiKey"];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // When no key is configured, allow all requests (dev-safe fallback).
        if (string.IsNullOrEmpty(_configuredKey))
        {
            await _next(context);
            return;
        }

        // Try X-Api-Key header first, then Authorization: Bearer <key>.
        string? suppliedKey = null;

        if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValues))
        {
            suppliedKey = apiKeyValues.ToString();
        }
        else if (context.Request.Headers.TryGetValue(AuthorizationHeaderName, out var authValues))
        {
            var authHeader = authValues.ToString();
            if (authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                suppliedKey = authHeader[BearerPrefix.Length..].Trim();
            }
        }

        // Constant-time comparison to prevent timing attacks.
        if (suppliedKey is null
            || suppliedKey.Length != _configuredKey.Length
            || !CryptographicEquals(suppliedKey, _configuredKey))
        {
            _logger.LogWarning("[McpAuth] Rejected request to {Path} — invalid or missing API key.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Fixed-time comparison that does not short-circuit, preventing timing side channels.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }
}
