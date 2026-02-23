using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.BuildingBlocks.Domain.Services;

namespace OpsCopilot.AgentRuns.Presentation.Endpoints;

public static class AgentRunEndpoints
{
    // Compact serializer for the compatibility bridge: AlertPayloadDto → JSON string.
    // Used to produce the stable string consumed by AlertFingerprintService.Compute().
    private static readonly JsonSerializerOptions BridgeJsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static IEndpointRouteBuilder MapAgentRunEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /agent/triage
        // Header  (required): x-tenant-id
        // Body    (JSON)     : TriageRequest { AlertPayload: AlertPayloadDto, TimeRangeMinutes }
        //
        // Validation:
        //   • x-tenant-id header present and non-empty
        //   • AlertPayload object required (enforced by model binding)
        //   • AlertPayload.AlertSource non-empty
        //   • AlertPayload.Fingerprint non-empty
        //   • TimeRangeMinutes in [1, 1440]
        app.MapPost("/agent/triage", async (
            HttpContext        httpContext,
            TriageRequest      request,
            TriageOrchestrator orchestrator,
            IConfiguration     config,
            CancellationToken  ct) =>
        {
            // ── Header validation ───────────────────────────────────────────
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail:     "The 'x-tenant-id' header is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing required header");

            // ── Body validation ─────────────────────────────────────────────
            // AlertPayload object itself: null if body was empty / wrong content-type.
            if (request.AlertPayload is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["AlertPayload"] = ["AlertPayload is required."],
                });

            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.AlertPayload.AlertSource))
                errors["AlertPayload.AlertSource"] =
                    ["AlertSource is required and must not be empty or whitespace."];

            if (string.IsNullOrWhiteSpace(request.AlertPayload.Fingerprint))
                errors["AlertPayload.Fingerprint"] =
                    ["Fingerprint is required and must not be empty or whitespace."];

            if (request.TimeRangeMinutes is < 1 or > 1440)
                errors["TimeRangeMinutes"] =
                    ["TimeRangeMinutes must be between 1 and 1440 (24 hours)."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // ── Workspace ID (request body → config fallback) ────────────────
            var workspaceId = !string.IsNullOrWhiteSpace(request.WorkspaceId)
                ? request.WorkspaceId
                : config["WORKSPACE_ID"];

            if (string.IsNullOrWhiteSpace(workspaceId))
                return Results.Problem(
                    detail:     "Supply 'workspaceId' in the request body or set the WORKSPACE_ID "
                              + "environment variable / config entry. "
                              + "For local dev: dotnet user-secrets set WORKSPACE_ID \"<your-guid>\"",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing WorkspaceId");

            if (!Guid.TryParse(workspaceId, out _))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["WorkspaceId"] = [$"'{workspaceId}' is not a valid GUID."],
                });

            // ── Compatibility bridge ────────────────────────────────────────
            // The application layer (AlertFingerprintService) currently expects
            // a raw JSON string. Serialize the typed DTO to compact JSON here so
            // no application-layer contract changes are needed in this slice.
            // TODO: remove this bridge when TriageOrchestrator accepts AlertPayloadDto directly.
            var alertPayloadJson = JsonSerializer.Serialize(request.AlertPayload, BridgeJsonOpts);
            var fingerprint      = AlertFingerprintService.Compute(alertPayloadJson);

            // ── Orchestrate ─────────────────────────────────────────────────
            TriageResult result;
            try
            {
                result = await orchestrator.RunAsync(
                    tenantId,
                    fingerprint,
                    workspaceId,
                    request.TimeRangeMinutes,
                    request.AlertPayload.Title,
                    request.SessionId,
                    ct);
            }
            catch (SessionTenantMismatchException ex)
            {
                return Results.Problem(
                    detail:     ex.Message,
                    statusCode: StatusCodes.Status403Forbidden,
                    title:      "Session tenant mismatch");
            }

            var citations = result.Citations
                .Select(c => new CitationDto(
                    c.WorkspaceId,
                    c.ExecutedQuery,
                    c.Timespan,
                    c.ExecutedAtUtc))
                .ToList();

            var runbookCitations = result.RunbookCitations
                .Select(c => new RunbookCitationDto(
                    c.RunbookId,
                    c.Title,
                    c.Snippet,
                    c.Score))
                .ToList();

            // Parse the summary JSON string into a structured JsonElement
            // to prevent double-encoding in the HTTP response.
            JsonElement? summary = null;
            if (result.SummaryJson is not null)
            {
                using var doc = JsonDocument.Parse(result.SummaryJson);
                summary = doc.RootElement.Clone();
            }

            return Results.Ok(new TriageResponse(
                result.RunId,
                result.Status.ToString(),
                summary,
                citations,
                runbookCitations,
                result.SessionId,
                result.IsNewSession,
                result.SessionExpiresAtUtc,
                result.UsedSessionContext));
        })
        .WithName("PostTriage")
        .WithTags("AgentRuns")
        .Accepts<TriageRequest>("application/json")
        .Produces<TriageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
