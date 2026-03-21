using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Reporting.Application.Abstractions;

namespace OpsCopilot.Reporting.Presentation.Endpoints;

public static class AgentRunsReportingEndpoints
{
    public static IEndpointRouteBuilder MapAgentRunsReportingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports/agent-runs")
                       .WithTags("Agent Runs Reporting");

        group.MapGet("/summary",    GetSummary);
        group.MapGet("/trend",      GetTrend);
        group.MapGet("/tool-usage", GetToolUsage);

        return app;
    }

    // ── GET /reports/agent-runs/summary ──────────────────────────────
    private static async Task<IResult> GetSummary(
        HttpContext ctx,
        IAgentRunsReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantId))
            return Results.Problem(
                detail:     "The 'x-tenant-id' header is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title:      "Missing required header");

        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetSummaryAsync(from, to, tenantId, ct);
        return Results.Ok(result);
    }

    // ── GET /reports/agent-runs/trend ────────────────────────────────
    private static async Task<IResult> GetTrend(
        HttpContext ctx,
        IAgentRunsReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantId))
            return Results.Problem(
                detail:     "The 'x-tenant-id' header is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title:      "Missing required header");

        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetTrendAsync(from, to, tenantId, ct);
        return Results.Ok(result);
    }

    // ── GET /reports/agent-runs/tool-usage ───────────────────────────
    private static async Task<IResult> GetToolUsage(
        HttpContext ctx,
        IAgentRunsReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantId))
            return Results.Problem(
                detail:     "The 'x-tenant-id' header is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title:      "Missing required header");

        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetToolUsageAsync(from, to, tenantId, ct);
        return Results.Ok(result);
    }

    // ── Date-range validation ─────────────────────────────────────────
    private static bool TryParseDateRange(
        string? fromUtcRaw, string? toUtcRaw,
        out DateTime? from, out DateTime? to,
        out string? error)
    {
        from  = null;
        to    = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(fromUtcRaw))
        {
            if (!DateTime.TryParse(fromUtcRaw, out var f))
            {
                error = "Invalid fromUtc date format.";
                return false;
            }
            from = f;
        }

        if (!string.IsNullOrWhiteSpace(toUtcRaw))
        {
            if (!DateTime.TryParse(toUtcRaw, out var t))
            {
                error = "Invalid toUtc date format.";
                return false;
            }
            to = t;
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            error = "fromUtc must not be after toUtc.";
            return false;
        }

        return true;
    }
}
