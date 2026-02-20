using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AlertIngestion.Domain.Services;

namespace OpsCopilot.AgentRuns.Presentation.Endpoints;

public static class AgentRunEndpoints
{
    public static IEndpointRouteBuilder MapAgentRunEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /agent/triage
        // Headers (required): x-tenant-id
        // Body (JSON): TriageRequest
        app.MapPost("/agent/triage", async (
            HttpContext          httpContext,
            TriageRequest        request,
            TriageOrchestrator   orchestrator,
            IConfiguration       config,
            CancellationToken    ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("Missing required header: x-tenant-id");

            if (string.IsNullOrWhiteSpace(request.AlertPayload))
                return Results.BadRequest("AlertPayload must not be empty.");

            if (request.TimeRangeMinutes is < 1 or > 10_080)
                return Results.BadRequest("TimeRangeMinutes must be between 1 and 10080.");

            var workspaceId = config["WORKSPACE_ID"]
                ?? throw new InvalidOperationException(
                    "WORKSPACE_ID is not configured.");

            var fingerprint = AlertFingerprintService.Compute(request.AlertPayload);

            var result = await orchestrator.RunAsync(
                tenantId,
                fingerprint,
                workspaceId,
                request.TimeRangeMinutes,
                ct);

            var citations = result.Citations
                .Select(c => new CitationDto(
                    c.WorkspaceId,
                    c.ExecutedQuery,
                    c.Timespan,
                    c.ExecutedAtUtc))
                .ToList();

            var response = new TriageResponse(
                result.RunId,
                result.Status.ToString(),
                result.SummaryJson,
                citations);

            return Results.Ok(response);
        })
        .WithName("PostTriage")
        .WithTags("AgentRuns")
        .Accepts<TriageRequest>("application/json")
        .Produces<TriageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
