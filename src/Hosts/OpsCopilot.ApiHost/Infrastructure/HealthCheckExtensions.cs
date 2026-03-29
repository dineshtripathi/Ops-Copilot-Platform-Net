using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System.Text.Json;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 140 — Registers live/ready health probes for Container Apps.
/// /healthz/live  → fast liveness  (process responsive; no dep checks)
/// /healthz/ready → readiness      (SQL + optional Redis)
/// /healthz       → compat alias   (same as liveness)
/// </summary>
internal static class HealthCheckExtensions
{
    internal static IServiceCollection AddOpsCopilotHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var hcBuilder = services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API is responsive"), tags: ["live"]);

        // SQL readiness check — requires ConnectionStrings:Sql or SQL_CONNECTION_STRING
        var sqlConn = configuration["ConnectionStrings:Sql"]
                   ?? configuration["SQL_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(sqlConn))
        {
            hcBuilder.AddCheck<SqlConnectionHealthCheck>(
                "sql",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);
            services.AddSingleton(new SqlHealthCheckOptions(sqlConn));
        }

        // Redis readiness check — optional; only when Redis session store is configured
        var sessionProvider = configuration["AgentRuns:SessionStore:Provider"] ?? string.Empty;
        var redisConn = configuration["AgentRuns:SessionStore:ConnectionString"];
        if (string.Equals(sessionProvider, "Redis", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(redisConn))
        {
            hcBuilder.AddCheck<RedisConnectionHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);
            services.AddSingleton(new RedisHealthCheckOptions(redisConn));
        }

        return services;
    }

    internal static WebApplication MapOpsCopilotHealthChecks(this WebApplication app)
    {
        // Liveness: is the process up?
        app.MapHealthChecks("/healthz/live", new HealthCheckOptions
        {
            Predicate       = c => c.Tags.Contains("live"),
            ResponseWriter  = WriteHealthResponse
        }).ExcludeFromDescription();

        // Readiness: are dependencies responsive?
        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate       = c => c.Tags.Contains("ready") || c.Tags.Contains("live"),
            ResponseWriter  = WriteHealthResponse
        }).ExcludeFromDescription();

        // Backwards-compat alias → liveness only
        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate       = c => c.Tags.Contains("live"),
            ResponseWriter  = WriteHealthResponse
        }).ExcludeFromDescription();

        return app;
    }

    // ── JSON response writer ─────────────────────────────────────────────────

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            status   = report.Status.ToString().ToLowerInvariant(),
            duration = report.TotalDuration.TotalMilliseconds,
            entries  = report.Entries.Select(e => new
            {
                name     = e.Key,
                status   = e.Value.Status.ToString().ToLowerInvariant(),
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        return context.Response.WriteAsync(payload);
    }
}

// ── Health check options carriers ────────────────────────────────────────────

internal sealed record SqlHealthCheckOptions(string ConnectionString);
internal sealed record RedisHealthCheckOptions(string ConnectionString);

// ── IHealthCheck implementations ─────────────────────────────────────────────

internal sealed class SqlConnectionHealthCheck(SqlHealthCheckOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqlConnection(options.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQL connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL connection failed", ex);
        }
    }
}

internal sealed class RedisConnectionHealthCheck(RedisHealthCheckOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var mux = await ConnectionMultiplexer.ConnectAsync(options.ConnectionString);
            var db = mux.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis connection failed", ex);
        }
    }
}
