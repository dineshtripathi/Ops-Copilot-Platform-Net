using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Domain.Models;
using OpsCopilot.BuildingBlocks.Contracts.Evaluation;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.BuildingBlocks.Contracts.Prompting;

namespace OpsCopilot.AgentRuns.Presentation.Endpoints;

public static class AgentRunEndpoints
{
    // Compact serializer for SSE streaming output (error and response events).
    private static readonly JsonSerializerOptions SseJsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Extracts subscription ID and resource group from an ARM resource ID string.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/...
    /// Returns (null, null) if the path is absent or malformed.
    /// </summary>
    /// <summary>
    /// Splits <paramref name="text"/> into sentence-level chunks for progressive SSE delivery.
    /// Splits on ". ", "! ", and "? " boundaries; collapses excessively short fragments.
    /// </summary>
    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        var delimiters = new[] { ". ", "! ", "? " };
        var remaining  = text.Trim();

        while (remaining.Length > 0)
        {
            int splitAt = -1;
            foreach (var d in delimiters)
            {
                int idx = remaining.IndexOf(d, StringComparison.Ordinal);
                if (idx >= 0 && (splitAt < 0 || idx < splitAt))
                    splitAt = idx + d.Length - 1; // include the punctuation, exclude trailing space
            }

            if (splitAt < 0)
            {
                yield return remaining;
                yield break;
            }

            // Include the punctuation mark, exclude the trailing space used as delimiter
            var sentence = remaining[..(splitAt + 1)].TrimEnd();
            if (sentence.Length > 0)
                yield return sentence;

            remaining = remaining[(splitAt + 1)..].TrimStart();
        }
    }

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
            ITriageOrchestrator   orchestrator,
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

            // ── Fingerprint ─────────────────────────────────────────────────
            // Use the caller-supplied fingerprint from the alert payload.
            // The source system embeds this to correlate repeated firings.
            var fingerprint = request.AlertPayload.Fingerprint;

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

            // ── Token budget pre-flight check ────────────────────────────────
            if (request.SessionId.HasValue)
            {
                var accumulator = httpContext.RequestServices.GetRequiredService<ITokenUsageAccumulator>();
                var budgetPolicy = httpContext.RequestServices.GetRequiredService<ITokenBudgetPolicy>();
                var cap = budgetPolicy.CheckRunBudget(tenantId, Guid.Empty).MaxTokens;
                if (cap.HasValue)
                {
                    var usedTokens = accumulator.GetTotalTokens(tenantId, request.SessionId.Value.ToString());
                    if (usedTokens >= cap.Value)
                        return Results.Problem(
                            detail:     $"Token budget exhausted for this session. Used: {usedTokens}, Max: {cap.Value}",
                            statusCode: StatusCodes.Status429TooManyRequests,
                            title:      "Token Budget Exceeded");
                }
            }

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

            // ── Record token usage for session budget tracking ───────────────
            if (result.TotalTokens.HasValue && request.SessionId.HasValue)
            {
                var accumulator = httpContext.RequestServices.GetRequiredService<ITokenUsageAccumulator>();
                accumulator.AddTokens(tenantId, request.SessionId.Value.ToString(), result.TotalTokens.Value);
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
                EstimatedCost:               result.EstimatedCost,
                LlmNarrative:                result.LlmNarrative,
                WasDeduplicated:             result.WasDeduplicated));
        })
        .WithName("PostTriage")
        .WithTags("AgentRuns")
        .Accepts<TriageRequest>("application/json")
        .Produces<TriageResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST /agent/chat
        // Header  (required): x-tenant-id
        // Body    (JSON)     : ChatRequest { Query }
        //
        // Returns a grounded answer from incident memory + runbook search.
        app.MapPost("/agent/chat", async (
            HttpContext         httpContext,
            ChatRequest         request,
            ChatOrchestrator    chatOrchestrator,
            CancellationToken   ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail: "The x-tenant-id header is required.",
                    statusCode: StatusCodes.Status400BadRequest);

            if (string.IsNullOrWhiteSpace(request?.Query))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["Query"] = ["Query is required and must not be empty."]
                    });

            var result = await chatOrchestrator.ChatAsync(tenantId, request.Query, ct);

            var memoryCitations = result.MemoryCitations
                .Select(m => new MemoryCitationDto(
                    m.RunId, m.AlertFingerprint, m.SummarySnippet,
                    m.Score, m.CreatedAtUtc))
                .ToList();

            var runbookCitations = result.RunbookCitations
                .Select(r => new RunbookCitationDto(r.RunbookId, r.Title, r.Snippet, r.Score))
                .ToList();

            return Results.Ok(new ChatResponse(result.Answer, memoryCitations, runbookCitations));
        })
        .WithName("PostChat")
        .WithTags("AgentRuns")
        .Accepts<ChatRequest>("application/json")
        .Produces<ChatResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /agent/chat/stream
        // Header (required): x-tenant-id
        // Body   (JSON)    : ChatRequest { Query }
        //
        // Streams the LLM response as Server-Sent Events using AG-UI delta protocol:
        //   RunStarted → TextMessageStart → TextMessageContent* → TextMessageEnd → RunFinished
        app.MapPost("/agent/chat/stream", async (
            HttpContext      httpContext,
            ChatRequest      request,
            ChatOrchestrator chatOrchestrator,
            CancellationToken ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail:     "The x-tenant-id header is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing required header");

            if (string.IsNullOrWhiteSpace(request?.Query))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["Query"] = ["Query is required and must not be empty."]
                    });

            // ── Begin SSE response ───────────────────────────────────────────
            httpContext.Response.StatusCode  = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers["Cache-Control"]     = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            var chatRunId = Guid.NewGuid().ToString();
            var chatMsgId = Guid.NewGuid().ToString();

            // AG-UI event 1: RunStarted
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new RunStartedEvent("RunStarted", chatRunId), SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            // AG-UI event 2: TextMessageStart
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new TextMessageStartEvent("TextMessageStart", chatMsgId, "assistant"), SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            // AG-UI events 3…N: TextMessageContent — one per streamed token delta
            try
            {
                await foreach (var delta in chatOrchestrator.ChatStreamingAsync(tenantId, request.Query, ct))
                {
                    await httpContext.Response.WriteAsync(
                        "data: " + JsonSerializer.Serialize(
                            new TextMessageContentEvent("TextMessageContent", chatMsgId, delta), SseJsonOpts) + "\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — close stream cleanly.
                return Results.Empty;
            }

            // AG-UI event N+1: TextMessageEnd
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new TextMessageEndEvent("TextMessageEnd", chatMsgId), SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            // AG-UI event N+2: RunFinished (lightweight — no full triage payload)
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new RunFinishedEvent("RunFinished", chatRunId, null!), SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            return Results.Empty;
        })
        .WithName("PostChatStream")
        .WithTags("AgentRuns")
        .Accepts<ChatRequest>("application/json")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /agent/triage/stream
        // Same contract as POST /agent/triage but uses Server-Sent Events.
        // Immediately yields {"event":"started"} so the client knows the server is alive,
        // then {"event":"completed","response":{...}} when orchestration finishes.
        app.MapPost("/agent/triage/stream", async (
            HttpContext              httpContext,
            TriageRequest            request,
            ITriageOrchestrator      orchestrator,
            IPackTriageEnricher      packTriageEnricher,
            IPackEvidenceExecutor    packEvidenceExecutor,
            IPackSafeActionProposer  packSafeActionProposer,
            IPackSafeActionRecorder  packSafeActionRecorder,
            IConfiguration           config,
            CancellationToken        ct) =>
        {
            // ── Header validation ────────────────────────────────────────────
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail:     "The 'x-tenant-id' header is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing required header");

            if (request.AlertPayload is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["AlertPayload"] = ["AlertPayload is required."],
                });

            var streamErrors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.AlertPayload.AlertSource))
                streamErrors["AlertPayload.AlertSource"] =
                    ["AlertSource is required and must not be empty or whitespace."];
            if (string.IsNullOrWhiteSpace(request.AlertPayload.Fingerprint))
                streamErrors["AlertPayload.Fingerprint"] =
                    ["Fingerprint is required and must not be empty or whitespace."];
            if (request.TimeRangeMinutes is < 1 or > 1440)
                streamErrors["TimeRangeMinutes"] =
                    ["TimeRangeMinutes must be between 1 and 1440 (24 hours)."];
            if (streamErrors.Count > 0)
                return Results.ValidationProblem(streamErrors);

            var streamWorkspaceId = !string.IsNullOrWhiteSpace(request.WorkspaceId)
                ? request.WorkspaceId
                : config["WORKSPACE_ID"];
            if (string.IsNullOrWhiteSpace(streamWorkspaceId))
                return Results.Problem(
                    detail:     "Supply 'workspaceId' in the request body or set the WORKSPACE_ID "
                              + "environment variable / config entry.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing WorkspaceId");
            if (!Guid.TryParse(streamWorkspaceId, out _))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["WorkspaceId"] = [$"'{streamWorkspaceId}' is not a valid GUID."],
                });

            var streamFingerprint = request.AlertPayload.Fingerprint;

            var (streamSubId, streamRg) = ParseArmResourceId(request.AlertPayload.ResourceId);
            var streamRunContext = new RunContext(
                AlertProvider:       request.AlertPayload.AlertSource,
                AlertSourceType:     request.AlertPayload.SignalType,
                AzureResourceId:     request.AlertPayload.ResourceId,
                AzureApplication:    request.AlertPayload.ServiceName,
                AzureWorkspaceId:    streamWorkspaceId,
                AzureSubscriptionId: streamSubId,
                AzureResourceGroup:  streamRg);

            // ── Token budget pre-flight check ────────────────────────────────
            if (request.SessionId.HasValue)
            {
                var streamAccumulator = httpContext.RequestServices.GetRequiredService<ITokenUsageAccumulator>();
                var streamBudgetPolicy = httpContext.RequestServices.GetRequiredService<ITokenBudgetPolicy>();
                var streamCap = streamBudgetPolicy.CheckRunBudget(tenantId, Guid.Empty).MaxTokens;
                if (streamCap.HasValue)
                {
                    var streamUsed = streamAccumulator.GetTotalTokens(tenantId, request.SessionId.Value.ToString());
                    if (streamUsed >= streamCap.Value)
                        return Results.Problem(
                            detail:     $"Token budget exhausted for this session. Used: {streamUsed}, Max: {streamCap.Value}",
                            statusCode: StatusCodes.Status429TooManyRequests,
                            title:      "Token Budget Exceeded");
                }
            }

            // ── Begin SSE response ───────────────────────────────────────────
            httpContext.Response.StatusCode  = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers["Cache-Control"]     = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            var agUiRunId = Guid.NewGuid().ToString();

            // AG-UI event 1: RunStarted
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new RunStartedEvent("RunStarted", agUiRunId), SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            // ── Orchestrate ──────────────────────────────────────────────────
            TriageResult streamResult;
            try
            {
                streamResult = await orchestrator.RunAsync(
                    tenantId, streamFingerprint, streamWorkspaceId, request.TimeRangeMinutes,
                    alertTitle:     request.AlertPayload.Title,
                    subscriptionId: null,
                    resourceGroup:  null,
                    sessionId:      request.SessionId,
                    context:        streamRunContext,
                    ct:             ct);
            }
            catch (SessionTenantMismatchException ex)
            {
                await httpContext.Response.WriteAsync(
                    "data: " + JsonSerializer.Serialize(
                        new RunErrorEvent("RunError", agUiRunId, ex.Message), SseJsonOpts) + "\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
                return Results.Empty;
            }

            // ── Record token usage for session budget tracking ───────────────
            if (streamResult.TotalTokens.HasValue && request.SessionId.HasValue)
            {
                var streamAccumulator = httpContext.RequestServices.GetRequiredService<ITokenUsageAccumulator>();
                streamAccumulator.AddTokens(tenantId, request.SessionId.Value.ToString(), streamResult.TotalTokens.Value);
            }

            // ── Build full response (same shape as PostTriage) ───────────────
            var streamCitations = streamResult.Citations
                .Select(c => new CitationDto(c.WorkspaceId, c.ExecutedQuery, c.Timespan, c.ExecutedAtUtc))
                .ToList();
            var streamRunbookCitations = streamResult.RunbookCitations
                .Select(c => new RunbookCitationDto(c.RunbookId, c.Title, c.Snippet, c.Score))
                .ToList();

            var streamDeploymentMode = config["Packs:DeploymentMode"] ?? "A";
            var streamPackEnrichment = await packTriageEnricher.EnrichAsync(ct);
            var streamEvidenceResult = await packEvidenceExecutor.ExecuteAsync(
                new PackEvidenceExecutionRequest(streamDeploymentMode, tenantId), ct);
            var streamProposalResult = await packSafeActionProposer.ProposeAsync(
                new PackSafeActionProposalRequest(streamDeploymentMode, tenantId), ct);

            var streamPackRunbooks = streamPackEnrichment.PackRunbooks
                .Select(r => new PackRunbookDto(r.PackName, r.RunbookId, r.File, r.ContentSnippet))
                .ToList();
            var streamPackEvidenceCollectors = streamPackEnrichment.PackEvidenceCollectors
                .Select(e => new PackEvidenceCollectorDto(
                    e.PackName, e.EvidenceCollectorId, e.RequiredMode, e.QueryFile, e.KqlContent))
                .ToList();
            var streamPackEvidenceResults = streamEvidenceResult.EvidenceItems
                .Select(e => new PackEvidenceResultDto(
                    e.PackName, e.CollectorId, e.ConnectorName,
                    e.QueryFile, e.QueryContent, e.ResultJson, e.RowCount, e.ErrorMessage))
                .ToList();
            var streamPackSafeActionProposals = streamProposalResult.Proposals
                .Select(p => new PackSafeActionProposalDto(
                    p.PackName, p.ActionId, p.DisplayName, p.ActionType,
                    p.RequiresMode, p.DefinitionFile, p.ParametersJson,
                    p.ErrorMessage, p.IsExecutableNow, p.ExecutionBlockedReason,
                    p.GovernanceAllowed, p.GovernanceReasonCode, p.GovernanceMessage,
                    p.ScopeAllowed, p.ScopeReasonCode, p.ScopeMessage,
                    p.DefinitionValidationErrorCode, p.DefinitionValidationMessage,
                    p.OperatorPreview))
                .ToList();

            var streamRecordResult = await packSafeActionRecorder.RecordAsync(
                new PackSafeActionRecordRequest(
                    streamDeploymentMode, tenantId, streamResult.RunId, streamProposalResult.Proposals),
                ct);

            PackSafeActionRecordSummaryDto? streamRecordSummary =
                streamRecordResult.Records.Count > 0
                    ? new PackSafeActionRecordSummaryDto(
                        streamRecordResult.Records
                            .Select(r => new PackSafeActionRecordItemDto(
                                r.PackName, r.ActionId, r.ActionType,
                                r.ActionRecordId, r.Status, r.ErrorMessage, r.PolicyDenialReasonCode))
                            .ToList(),
                        streamRecordResult.CreatedCount,
                        streamRecordResult.SkippedCount,
                        streamRecordResult.FailedCount,
                        streamRecordResult.Errors)
                    : null;

            JsonElement? streamSummary = null;
            if (streamResult.SummaryJson is not null)
            {
                using var doc = JsonDocument.Parse(streamResult.SummaryJson);
                streamSummary = doc.RootElement.Clone();
            }

            var streamResponse = new TriageResponse(
                streamResult.RunId,
                streamResult.Status.ToString(),
                streamSummary,
                streamCitations,
                streamRunbookCitations,
                streamResult.SessionId,
                streamResult.IsNewSession,
                streamResult.SessionExpiresAtUtc,
                streamResult.UsedSessionContext,
                SessionReasonCode:           streamResult.SessionReasonCode,
                PackRunbooks:                streamPackRunbooks,
                PackEvidenceCollectors:      streamPackEvidenceCollectors,
                PackErrors:                  streamPackEnrichment.PackErrors.Count > 0
                    ? streamPackEnrichment.PackErrors : null,
                PackEvidenceResults:         streamPackEvidenceResults.Count > 0
                    ? streamPackEvidenceResults : null,
                PackSafeActionProposals:     streamPackSafeActionProposals.Count > 0
                    ? streamPackSafeActionProposals : null,
                PackSafeActionRecordSummary: streamRecordSummary,
                ModelId:                     streamResult.ModelId,
                PromptVersionId:             streamResult.PromptVersionId,
                InputTokens:                 streamResult.InputTokens,
                OutputTokens:                streamResult.OutputTokens,
                TotalTokens:                 streamResult.TotalTokens,
                EstimatedCost:               streamResult.EstimatedCost,
                LlmNarrative:                streamResult.LlmNarrative,
                WasDeduplicated:             streamResult.WasDeduplicated);

            // AG-UI events 2–4: TextMessageStart / TextMessageContent / TextMessageEnd
            // Emitted only when the orchestrator produced a non-empty LLM narrative.
            if (!string.IsNullOrWhiteSpace(streamResponse.LlmNarrative))
            {
                var agUiMsgId = Guid.NewGuid().ToString();

                await httpContext.Response.WriteAsync(
                    "data: " + JsonSerializer.Serialize(
                        new TextMessageStartEvent("TextMessageStart", agUiMsgId, "assistant"),
                        SseJsonOpts) + "\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);

                // Split narrative into sentence-level chunks for progressive rendering.
                var chunks = SplitIntoChunks(streamResponse.LlmNarrative);
                foreach (var chunk in chunks)
                {
                    await httpContext.Response.WriteAsync(
                        "data: " + JsonSerializer.Serialize(
                            new TextMessageContentEvent("TextMessageContent", agUiMsgId, chunk),
                            SseJsonOpts) + "\n\n", ct);
                }
                await httpContext.Response.Body.FlushAsync(ct);

                await httpContext.Response.WriteAsync(
                    "data: " + JsonSerializer.Serialize(
                        new TextMessageEndEvent("TextMessageEnd", agUiMsgId),
                        SseJsonOpts) + "\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            // AG-UI event 5: RunFinished (carries full triage response)
            await httpContext.Response.WriteAsync(
                "data: " + JsonSerializer.Serialize(
                    new RunFinishedEvent("RunFinished", agUiRunId, streamResponse),
                    SseJsonOpts) + "\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            return Results.Empty;
        })
        .WithName("PostTriageStream")
        .WithTags("AgentRuns")
        .Accepts<TriageRequest>("application/json")
        .ProducesProblem(StatusCodes.Status400BadRequest);

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

    public static IEndpointRouteBuilder MapFeedbackEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /agent/runs/{runId}/feedback
        // Header  (required): x-tenant-id
        // Body    (JSON)     : SubmitFeedbackRequest { Rating, Comment? }
        //
        // Validation:
        //   • x-tenant-id header present and non-empty
        //   • runId must be a valid Guid (enforced by route constraint)
        //   • Rating must be 1–5
        //   • Run must exist and belong to the tenant (404 / 403)
        //   • Only one feedback allowed per run (409 Conflict)
        app.MapPost("/agent/runs/{runId:guid}/feedback", async (
            Guid                    runId,
            HttpContext             httpContext,
            SubmitFeedbackRequest   request,
            IAgentRunRepository     runRepository,
            IFeedbackQualityGate    qualityGate,
            IRunEvalSink            evalSink,
            CancellationToken       ct) =>
        {
            // ── Header validation ────────────────────────────────────────────
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.Problem(
                    detail:     "The 'x-tenant-id' header is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Missing required header");

            // ── Body validation ──────────────────────────────────────────────
            if (request.Rating is < 1 or > 5)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Rating"] = ["Rating must be between 1 (poor) and 5 (excellent)."],
                });

            // ── Duplicate check ──────────────────────────────────────────────
            if (await runRepository.FeedbackExistsAsync(runId, ct))
                return Results.Problem(
                    detail:     $"Feedback for run {runId} has already been submitted.",
                    statusCode: StatusCodes.Status409Conflict,
                    title:      "Feedback already exists");

            // ── Persist ──────────────────────────────────────────────────────
            AgentRunFeedback feedback;
            try
            {
                feedback = await runRepository.SaveFeedbackAsync(
                    runId, tenantId, request.Rating, request.Comment, ct);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new { detail = ex.Message });
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("does not belong"))
            {
                return Results.Problem(
                    detail:     ex.Message,
                    statusCode: StatusCodes.Status403Forbidden,
                    title:      "Run tenant mismatch");
            }

            // ── Online eval recording ─────────────────────────────────────────────
            await evalSink.RecordAsync(new RunEvalRecord(
                RunId:               feedback.RunId,
                RetrievalConfidence: 0.0,
                FeedbackScore:       feedback.Rating / 5.0f,
                ModelVersion:        "unknown",
                PromptVersionId:     "unknown",
                RecordedAt:          feedback.SubmittedAtUtc), ct);

            // ── Canary promotion gate ─────────────────────────────────────────────
            var promotionDecision = qualityGate.Evaluate(
                promptKey:    "default",
                qualityScore: feedback.Rating / 5.0f);

            return Results.Created(
                $"/agent/runs/{runId}/feedback",
                new RunFeedbackResponse(
                    feedback.FeedbackId,
                    feedback.RunId,
                    feedback.Rating,
                    feedback.Comment,
                    feedback.SubmittedAtUtc,
                    promotionDecision));
        })
        .WithName("PostRunFeedback")
        .WithTags("AgentRuns")
        .Accepts<SubmitFeedbackRequest>("application/json")
        .Produces<RunFeedbackResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
