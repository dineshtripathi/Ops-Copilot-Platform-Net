using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.BuildingBlocks.Domain.Services;

namespace OpsCopilot.AgentRuns.Presentation.Endpoints;

public static class AgentRunEndpoints
{
    // Compact serializer for the compatibility bridge: AlertPayloadDto → JSON string.
    // Used to produce the stable string consumed by AlertFingerprintService.Compute().
    private static readonly JsonSerializerOptions BridgeJsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Extracts subscription ID and resource group from an ARM resource ID string.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/...
    /// Returns (null, null) if the path is absent or malformed.
    /// </summary>
    private static (string? SubscriptionId, string? ResourceGroup) ParseArmResourceId(string? resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return (null, null);
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? sub = null, rg = null;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
                sub = parts[i + 1];
            else if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                rg = parts[i + 1];
        }
        return (sub, rg);
    }

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
            HttpContext           httpContext,
            TriageRequest         request,
            TriageOrchestrator    orchestrator,
            IConfiguration        config,
            IPackTriageEnricher   packTriageEnricher,
            IPackEvidenceExecutor    packEvidenceExecutor,
            IPackSafeActionProposer  packSafeActionProposer,
            IPackSafeActionRecorder  packSafeActionRecorder,
            CancellationToken        ct) =>
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

            // ── Build run context from alert payload ─────────────────────────
            var (parsedSubId, parsedRg) = ParseArmResourceId(request.AlertPayload.ResourceId);
            var runContext = new RunContext(
                AlertProvider:       request.AlertPayload.AlertSource,
                AlertSourceType:     request.AlertPayload.SignalType,
                AzureResourceId:     request.AlertPayload.ResourceId,
                AzureApplication:    request.AlertPayload.ServiceName,
                AzureWorkspaceId:    workspaceId,
                AzureSubscriptionId: parsedSubId,
                AzureResourceGroup:  parsedRg);

            // ── Orchestrate ─────────────────────────────────────────────────
            TriageResult result;
            try
            {
                result = await orchestrator.RunAsync(
                    tenantId,
                    fingerprint,
                    workspaceId,
                    request.TimeRangeMinutes,
                    alertTitle:      request.AlertPayload.Title,
                    subscriptionId:  null,
                    resourceGroup:   null,
                    sessionId:       request.SessionId,
                    context:         runContext,
                    ct:              ct);
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

            // ── Pack enrichment (Mode A only) ─────────────────────────────
            var packEnrichment = await packTriageEnricher.EnrichAsync(ct);

            // ── Pack evidence execution (Mode B+) ───────────────────────────
            var deploymentMode = config["Packs:DeploymentMode"] ?? "A";
            var evidenceResult = await packEvidenceExecutor.ExecuteAsync(
                new PackEvidenceExecutionRequest(deploymentMode, tenantId), ct);

            // ── Pack safe-action proposals (Mode B+, propose-only) ──────────
            var proposalResult = await packSafeActionProposer.ProposeAsync(
                new PackSafeActionProposalRequest(deploymentMode, tenantId), ct);

            var packRunbooks = packEnrichment.PackRunbooks
                .Select(r => new PackRunbookDto(
                    r.PackName, r.RunbookId, r.File, r.ContentSnippet))
                .ToList();

            var packEvidenceCollectors = packEnrichment.PackEvidenceCollectors
                .Select(e => new PackEvidenceCollectorDto(
                    e.PackName, e.EvidenceCollectorId, e.RequiredMode,
                    e.QueryFile, e.KqlContent))
                .ToList();

            var packEvidenceResults = evidenceResult.EvidenceItems
                .Select(e => new PackEvidenceResultDto(
                    e.PackName, e.CollectorId, e.ConnectorName,
                    e.QueryFile, e.QueryContent, e.ResultJson,
                    e.RowCount, e.ErrorMessage))
                .ToList();

            var packSafeActionProposals = proposalResult.Proposals
                .Select(p => new PackSafeActionProposalDto(
                    p.PackName, p.ActionId, p.DisplayName, p.ActionType,
                    p.RequiresMode, p.DefinitionFile, p.ParametersJson,
                    p.ErrorMessage, p.IsExecutableNow, p.ExecutionBlockedReason,
                    p.GovernanceAllowed, p.GovernanceReasonCode, p.GovernanceMessage,
                    p.ScopeAllowed, p.ScopeReasonCode, p.ScopeMessage,
                    p.DefinitionValidationErrorCode, p.DefinitionValidationMessage, p.OperatorPreview))
                .ToList();

            // ── Pack safe-action recording (Mode C only) ────────────────────
            var recordResult = await packSafeActionRecorder.RecordAsync(
                new PackSafeActionRecordRequest(
                    deploymentMode, tenantId, result.RunId, proposalResult.Proposals),
                ct);

            PackSafeActionRecordSummaryDto? packSafeActionRecordSummary =
                recordResult.Records.Count > 0
                    ? new PackSafeActionRecordSummaryDto(
                        recordResult.Records
                            .Select(r => new PackSafeActionRecordItemDto(
                                r.PackName, r.ActionId, r.ActionType,
                                r.ActionRecordId, r.Status, r.ErrorMessage,
                                r.PolicyDenialReasonCode))
                            .ToList(),
                        recordResult.CreatedCount,
                        recordResult.SkippedCount,
                        recordResult.FailedCount,
                        recordResult.Errors)
                    : null;

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
                result.UsedSessionContext,
                SessionReasonCode:           result.SessionReasonCode,
                PackRunbooks:                packRunbooks,
                PackEvidenceCollectors:      packEvidenceCollectors,
                PackErrors:                  packEnrichment.PackErrors.Count > 0
                    ? packEnrichment.PackErrors
                    : null,
                PackEvidenceResults:         packEvidenceResults.Count > 0
                    ? packEvidenceResults
                    : null,
                PackSafeActionProposals:     packSafeActionProposals.Count > 0
                    ? packSafeActionProposals
                    : null,
                PackSafeActionRecordSummary: packSafeActionRecordSummary,
                ModelId:                     result.ModelId,
                PromptVersionId:             result.PromptVersionId,
                InputTokens:                 result.InputTokens,
                OutputTokens:                result.OutputTokens,
                TotalTokens:                 result.TotalTokens,
                EstimatedCost:               result.EstimatedCost));
        })
        .WithName("PostTriage")
        .WithTags("AgentRuns")
        .Accepts<TriageRequest>("application/json")
        .Produces<TriageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public static IEndpointRouteBuilder MapSessionEndpoints(
        this IEndpointRouteBuilder app)
    {
        // GET /session/{sessionId}
        // Header (required): x-tenant-id
        // Returns session metadata + last 10 run summaries.
        // Expired sessions return IsExpired: true rather than 404.
        app.MapGet("/session/{sessionId:guid}", async (
            Guid                sessionId,
            HttpContext         httpContext,
            ISessionStore       sessionStore,
            IAgentRunRepository runRepository,
            CancellationToken   ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail:     "The 'x-tenant-id' header is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing required header");

            var session = await sessionStore.GetIncludingExpiredAsync(sessionId, ct);
            if (session is null)
                return Results.NotFound();

            if (!string.Equals(session.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                return Results.Problem(
                    detail:     $"Session {sessionId} does not belong to the specified tenant.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title:      "Session tenant mismatch");

            var isExpired = DateTimeOffset.UtcNow > session.ExpiresAtUtc;
            var runs      = await runRepository.GetRecentRunsBySessionAsync(sessionId, limit: 10, ct);
            var runDtos   = runs
                .Select(r => new SessionRunSummaryDto(
                    r.RunId, r.Status.ToString(), r.AlertFingerprint, r.CreatedAtUtc))
                .ToList();

            return Results.Ok(new SessionResponse(
                session.SessionId,
                session.TenantId,
                isExpired,
                session.CreatedAtUtc,
                session.ExpiresAtUtc,
                runDtos));
        })
        .WithName("GetSession")
        .WithTags("AgentRuns")
        .Produces<SessionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
