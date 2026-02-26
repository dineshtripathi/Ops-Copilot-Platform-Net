using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Reporting.Application.Abstractions;

namespace OpsCopilot.Reporting.Presentation.Endpoints;

public static class ReportingEndpoints
{
    private const int DefaultRecentLimit = 20;
    private const int MaxRecentLimit     = 100;

    public static IEndpointRouteBuilder MapReportingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports/safe-actions")
                       .WithTags("Reporting");

        group.MapGet("/summary",        GetSummary);
        group.MapGet("/by-action-type", GetByActionType);
        group.MapGet("/by-tenant",      GetByTenant);
        group.MapGet("/recent",         GetRecent);

        return app;
    }

    // ── GET /reports/safe-actions/summary ────────────────────────────
    private static async Task<IResult> GetSummary(
        HttpContext ctx,
        IReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
        if (string.IsNullOrWhiteSpace(tenantId)) tenantId = null;

        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetSummaryAsync(from, to, tenantId, ct);
        return Results.Ok(result);
    }

    // ── GET /reports/safe-actions/by-action-type ─────────────────────
    private static async Task<IResult> GetByActionType(
        HttpContext ctx,
        IReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
        if (string.IsNullOrWhiteSpace(tenantId)) tenantId = null;

        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetByActionTypeAsync(from, to, tenantId, ct);
        return Results.Ok(result);
    }

    // ── GET /reports/safe-actions/by-tenant ──────────────────────────
    private static async Task<IResult> GetByTenant(
        HttpContext ctx,
        IReportingQueryService svc,
        string? fromUtc,
        string? toUtc,
        CancellationToken ct)
    {
        // by-tenant endpoint ignores x-tenant-id — it shows all tenants
        if (!TryParseDateRange(fromUtc, toUtc, out var from, out var to, out var error))
            return Results.BadRequest(error);

        var result = await svc.GetByTenantAsync(from, to, ct);
        return Results.Ok(result);
    }

    // ── GET /reports/safe-actions/recent ─────────────────────────────
    private static async Task<IResult> GetRecent(
        HttpContext ctx,
        IReportingQueryService svc,
        int? limit,
        CancellationToken ct)
    {
        var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
        if (string.IsNullOrWhiteSpace(tenantId)) tenantId = null;

        var effectiveLimit = Math.Clamp(limit ?? DefaultRecentLimit, 1, MaxRecentLimit);

        var result = await svc.GetRecentAsync(effectiveLimit, tenantId, ct);
        return Results.Ok(result);
    }

    // ── Date-range validation ───────────────────────────────────────
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
