using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Reporting.Application.Abstractions;

namespace OpsCopilot.Reporting.Presentation.Endpoints;

public static class PlatformReportingEndpoints
{
    public static IEndpointRouteBuilder MapPlatformReportingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports/platform")
                       .WithTags("Platform Reporting");

        group.MapGet("/evaluation-summary", GetEvaluationSummary);
        group.MapGet("/connectors",         GetConnectors);
        group.MapGet("/readiness",          GetReadiness);

        return app;
    }

    // ── GET /reports/platform/evaluation-summary ────────────────────
    private static async Task<IResult> GetEvaluationSummary(
        IPlatformReportingQueryService svc,
        CancellationToken ct)
    {
        var result = await svc.GetEvaluationSummaryAsync(ct);
        return Results.Ok(result);
    }

    // ── GET /reports/platform/connectors ────────────────────────────
    private static async Task<IResult> GetConnectors(
        IPlatformReportingQueryService svc,
        CancellationToken ct)
    {
        var result = await svc.GetConnectorInventoryAsync(ct);
        return Results.Ok(result);
    }

    // ── GET /reports/platform/readiness ─────────────────────────────
    private static async Task<IResult> GetReadiness(
        IPlatformReportingQueryService svc,
        CancellationToken ct)
    {
        var result = await svc.GetPlatformReadinessAsync(ct);
        return Results.Ok(result);
    }
}
