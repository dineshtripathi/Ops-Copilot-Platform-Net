using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 151 — Registers ASP.NET Core rate limiting (built-in, no extra NuGet).
/// Uses a GlobalLimiter so all API endpoints are protected without touching
/// individual Presentation-layer route registrations.
///
/// Two tiers are enforced:
///   • triage   — tight fixed window; applies to /agent/triage* and /ingest/alert*
///                (these paths spin an LLM + MCP child process per request)
///   • default  — relaxed fixed window; applies to every other API endpoint
///
/// Health probes (/healthz*) are always exempt.
///
/// The partition key is the authenticated user's NameIdentifier claim (set by
/// Slice 149 DevBypass or Entra JWT bearer). Falls back to remote IP, then
/// "anonymous" for fully unauthenticated callers (which the auth fallback policy
/// would normally reject before reaching any business endpoint).
///
/// Config:
///   RateLimiting:Triage:PermitLimit    — requests allowed per window (default 10)
///   RateLimiting:Triage:WindowSeconds  — window size in seconds      (default 60)
///   RateLimiting:Default:PermitLimit   — requests allowed per window (default 100)
///   RateLimiting:Default:WindowSeconds — window size in seconds      (default 60)
/// </summary>
internal static class RateLimitingExtensions
{
    // Paths that bypass rate limiting (health probes, anonymous by design)
    private static readonly string[] ExemptPrefixes = ["/healthz"];

    // Path prefixes that receive the tighter "triage" window
    private static readonly string[] TriagePrefixes =
    [
        "/agent/triage",
        "/ingest/alert"
    ];

    internal static IServiceCollection AddOpsCopilotRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var triageLimit   = configuration.GetValue<int>("RateLimiting:Triage:PermitLimit",      10);
        var triageWindow  = configuration.GetValue<int>("RateLimiting:Triage:WindowSeconds",    60);
        var defaultLimit  = configuration.GetValue<int>("RateLimiting:Default:PermitLimit",    100);
        var defaultWindow = configuration.GetValue<int>("RateLimiting:Default:WindowSeconds",  60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // ── Health probes are always exempt ──────────────────────────
                if (IsExempt(path))
                    return RateLimitPartition.GetNoLimiter("exempt");

                // ── Partition key: authenticated user → remote IP → anonymous ─
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? context.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous";

                // ── Triage tier (tight) ──────────────────────────────────────
                if (IsTriagePath(path))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"triage:{userId}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit          = triageLimit,
                            Window               = TimeSpan.FromSeconds(triageWindow),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit           = 0
                        });
                }

                // ── Default tier (relaxed) ───────────────────────────────────
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"default:{userId}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = defaultLimit,
                        Window               = TimeSpan.FromSeconds(defaultWindow),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = 0
                    });
            });
        });

        return services;
    }

    private static bool IsExempt(string path) =>
        Array.Exists(ExemptPrefixes, prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsTriagePath(string path) =>
        Array.Exists(TriagePrefixes, prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
