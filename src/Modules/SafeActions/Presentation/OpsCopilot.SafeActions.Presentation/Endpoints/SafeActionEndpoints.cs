using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Presentation.Contracts;
using OpsCopilot.SafeActions.Presentation.Identity;

namespace OpsCopilot.SafeActions.Presentation.Endpoints;

public static class SafeActionEndpoints
{
    private const int DefaultListLimit = 50;
    private const int MaxListLimit     = 200;

    public static IEndpointRouteBuilder MapSafeActionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/safe-actions")
                       .WithTags("SafeActions");

        // ── POST /safe-actions ──────────────────────────────────────
        group.MapPost("/", async (
            HttpContext ctx,
            SafeActionOrchestrator orchestrator,
            ProposeActionRequest request,
            CancellationToken ct) =>
        {
            var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("x-tenant-id header is required.");

            if (request.RunId == Guid.Empty)
                return Results.BadRequest("RunId is required.");

            if (string.IsNullOrWhiteSpace(request.ActionType))
                return Results.BadRequest("ActionType is required.");

            if (string.IsNullOrWhiteSpace(request.ProposedPayloadJson))
                return Results.BadRequest("ProposedPayloadJson is required.");

            try
            {
                var record = await orchestrator.ProposeAsync(
                    tenantId, request.RunId, request.ActionType,
                    request.ProposedPayloadJson, request.RollbackPayloadJson,
                    request.ManualRollbackGuidance, ct);

                var catalog  = ctx.RequestServices.GetService<IActionTypeCatalog>();
                var riskTier = catalog?.Get(record.ActionType)?.RiskTier.ToString();

                return Results.Created(
                    $"/safe-actions/{record.ActionRecordId}",
                    ActionRecordResponse.From(record, riskTier));
            }
            catch (PolicyDeniedException ex)
            {
                return Results.BadRequest(new { ex.ReasonCode, ex.Message });
            }
        })
        .WithName("ProposeAction")
        .Accepts<ProposeActionRequest>("application/json")
        .Produces<ActionRecordResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── GET /safe-actions/{id} ──────────────────────────────────
        group.MapGet("/{id:guid}", async (
            HttpContext ctx,
            Guid id,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            CancellationToken ct) =>
        {
            telemetry.RecordQueryRequest("detail");
            var record = await orchestrator.GetAsync(id, ct);
            if (record is null) return Results.NotFound();

            var ids = new[] { record.ActionRecordId } as IReadOnlyList<Guid>;
            var summaries = await orchestrator.GetAuditSummariesAsync(ids, ct);
            summaries.TryGetValue(record.ActionRecordId, out var audit);
            audit ??= AuditSummary.Empty;

            var approvals     = await orchestrator.GetApprovalsForActionAsync(record.ActionRecordId, ct);
            var executionLogs = await orchestrator.GetExecutionLogsForActionAsync(record.ActionRecordId, ct);

            var catalog  = ctx.RequestServices.GetService<IActionTypeCatalog>();
            var riskTier = catalog?.Get(record.ActionType)?.RiskTier.ToString();

            return Results.Ok(ActionRecordResponse.From(record, audit, approvals, executionLogs, riskTier));
        })
        .WithName("GetAction")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ── GET /safe-actions ───────────────────────────────────────
        group.MapGet("/", async (
            HttpContext ctx,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            Guid? runId,
            int? limit,
            string? actionType,
            string? status,
            string? rollbackStatus,
            bool? hasExecutionLogs,
            string? fromUtc,
            string? toUtc,
            CancellationToken ct) =>
        {
            telemetry.RecordQueryRequest("list");
            var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                telemetry.RecordQueryValidationFailure();
                return Results.BadRequest("x-tenant-id header is required.");
            }

            // ── Parse & validate optional enum filters ──────────
            ActionStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ActionStatus>(status, ignoreCase: true, out var s))
                {
                    telemetry.RecordQueryValidationFailure();
                    return Results.BadRequest($"Invalid status value: {status}");
                }
                parsedStatus = s;
            }

            RollbackStatus? parsedRollbackStatus = null;
            if (!string.IsNullOrWhiteSpace(rollbackStatus))
            {
                if (!Enum.TryParse<RollbackStatus>(rollbackStatus, ignoreCase: true, out var rs))
                {
                    telemetry.RecordQueryValidationFailure();
                    return Results.BadRequest($"Invalid rollbackStatus value: {rollbackStatus}");
                }
                parsedRollbackStatus = rs;
            }

            // ── Parse & validate optional date filters ──────────
            DateTimeOffset? parsedFromUtc = null;
            if (!string.IsNullOrWhiteSpace(fromUtc))
            {
                if (!DateTimeOffset.TryParse(fromUtc, out var f))
                {
                    telemetry.RecordQueryValidationFailure();
                    return Results.BadRequest($"Invalid fromUtc value: {fromUtc}");
                }
                parsedFromUtc = f;
            }

            DateTimeOffset? parsedToUtc = null;
            if (!string.IsNullOrWhiteSpace(toUtc))
            {
                if (!DateTimeOffset.TryParse(toUtc, out var t))
                {
                    telemetry.RecordQueryValidationFailure();
                    return Results.BadRequest($"Invalid toUtc value: {toUtc}");
                }
                parsedToUtc = t;
            }

            if (parsedFromUtc.HasValue && parsedToUtc.HasValue && parsedFromUtc > parsedToUtc)
            {
                telemetry.RecordQueryValidationFailure();
                return Results.BadRequest("fromUtc must not be after toUtc.");
            }

            var effectiveLimit = Math.Clamp(limit ?? DefaultListLimit, 1, MaxListLimit);

            IReadOnlyList<ActionRecord> records;

            var hasFilters = parsedStatus.HasValue || parsedRollbackStatus.HasValue
                || !string.IsNullOrWhiteSpace(actionType) || hasExecutionLogs.HasValue
                || parsedFromUtc.HasValue || parsedToUtc.HasValue;

            if (runId.HasValue && !hasFilters)
                records = await orchestrator.ListByRunAsync(runId.Value, ct);
            else if (hasFilters || !runId.HasValue)
                records = await orchestrator.QueryByTenantAsync(
                    tenantId, parsedStatus, parsedRollbackStatus, actionType,
                    hasExecutionLogs, parsedFromUtc, parsedToUtc, effectiveLimit, ct);
            else
                records = await orchestrator.ListByTenantAsync(
                    tenantId, effectiveLimit, ct);

            // ── Enrich with audit summaries ─────────────────────
            var ids = records.Select(r => r.ActionRecordId).ToList();
            var summaries = await orchestrator.GetAuditSummariesAsync(ids, ct);

            var catalog = ctx.RequestServices.GetService<IActionTypeCatalog>();

            var response = records.Select(r =>
            {
                summaries.TryGetValue(r.ActionRecordId, out var audit);
                var riskTier = catalog?.Get(r.ActionType)?.RiskTier.ToString();
                return ActionRecordResponse.From(r, audit ?? AuditSummary.Empty, riskTier);
            });

            return Results.Ok(response);
        })
        .WithName("ListActions")
        .Produces<IEnumerable<ActionRecordResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ── POST /safe-actions/{id}/approve ─────────────────────────
        group.MapPost("/{id:guid}/approve", async (
            HttpContext ctx,
            Guid id,
            ApproveActionRequest request,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            CancellationToken ct) =>
        {
            var resolver = ctx.RequestServices.GetRequiredService<IActorIdentityResolver>();
            var identity = resolver.Resolve(ctx);
            if (identity is null)
            {
                telemetry.RecordIdentityMissing401("approve");
                return Results.Unauthorized();
            }
            var actorId = identity.ActorId;

            try
            {
                var record = await orchestrator.ApproveAsync(
                    id, actorId, request.Reason, ct);
                telemetry.RecordApprovalDecision("approve");
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("ApproveAction")
        .Accepts<ApproveActionRequest>("application/json")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/reject ──────────────────────────
        group.MapPost("/{id:guid}/reject", async (
            HttpContext ctx,
            Guid id,
            ApproveActionRequest request,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            CancellationToken ct) =>
        {
            var resolver = ctx.RequestServices.GetRequiredService<IActorIdentityResolver>();
            var identity = resolver.Resolve(ctx);
            if (identity is null)
            {
                telemetry.RecordIdentityMissing401("reject");
                return Results.Unauthorized();
            }
            var actorId = identity.ActorId;

            try
            {
                var record = await orchestrator.RejectAsync(
                    id, actorId, request.Reason, ct);
                telemetry.RecordApprovalDecision("reject");
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("RejectAction")
        .Accepts<ApproveActionRequest>("application/json")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/execute ─────────────────────────
        // Guarded: returns 501 unless SafeActions:EnableExecution = true.
        // Throttled: returns 429 when execution throttle policy denies.
        group.MapPost("/{id:guid}/execute", async (
            Guid id,
            HttpContext httpContext,
            IConfiguration configuration,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            [FromServices] IExecutionThrottlePolicy throttlePolicy,
            [FromServices] ILogger<SafeActionOrchestrator> logger,
            CancellationToken ct) =>
        {
            if (!configuration.GetValue<bool>("SafeActions:EnableExecution"))
            {
                telemetry.RecordGuarded501("execute");
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }

            var actionRecord = await orchestrator.GetAsync(id, ct);
            if (actionRecord is null)
                return Results.NotFound();

            var decision = throttlePolicy.Evaluate(
                actionRecord.TenantId, actionRecord.ActionType, "execute");
            if (!decision.Allowed)
            {
                telemetry.RecordExecutionThrottled(
                    actionRecord.ActionType, actionRecord.TenantId, "execute");
                logger.LogWarning(
                    "Execution throttled for {ActionType} by {TenantId}, operation {OperationKind}, retry after {RetryAfterSeconds}s",
                    actionRecord.ActionType, actionRecord.TenantId, "execute", decision.RetryAfterSeconds);
                httpContext.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString();
                return Results.Json(
                    new { reasonCode = "throttled", message = decision.Message, retryAfterSeconds = decision.RetryAfterSeconds },
                    statusCode: StatusCodes.Status429TooManyRequests,
                    contentType: "application/json");
            }

            try
            {
                var record = await orchestrator.ExecuteAsync(id, ct);
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (PolicyDeniedException ex)
            {
                return Results.BadRequest(new { ex.ReasonCode, ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("ExecuteAction")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status501NotImplemented)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/rollback ────────────────────────
        group.MapPost("/{id:guid}/rollback", async (
            Guid id,
            SafeActionOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                var record = await orchestrator.RequestRollbackAsync(id, ct);
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("RequestRollback")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/rollback/approve ────────────────
        group.MapPost("/{id:guid}/rollback/approve", async (
            HttpContext ctx,
            Guid id,
            ApproveActionRequest request,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            CancellationToken ct) =>
        {
            var resolver = ctx.RequestServices.GetRequiredService<IActorIdentityResolver>();
            var identity = resolver.Resolve(ctx);
            if (identity is null)
            {
                telemetry.RecordIdentityMissing401("rollback_approve");
                return Results.Unauthorized();
            }
            var actorId = identity.ActorId;

            try
            {
                var record = await orchestrator.ApproveRollbackAsync(
                    id, actorId, request.Reason, ct);
                telemetry.RecordApprovalDecision("rollback_approve");
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("ApproveRollback")
        .Accepts<ApproveActionRequest>("application/json")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/rollback/execute ────────────────
        // Guarded: returns 501 unless SafeActions:EnableExecution = true.
        // Throttled: returns 429 when execution throttle policy denies.
        group.MapPost("/{id:guid}/rollback/execute", async (
            Guid id,
            HttpContext httpContext,
            IConfiguration configuration,
            SafeActionOrchestrator orchestrator,
            [FromServices] ISafeActionsTelemetry telemetry,
            [FromServices] IExecutionThrottlePolicy throttlePolicy,
            [FromServices] ILogger<SafeActionOrchestrator> logger,
            CancellationToken ct) =>
        {
            if (!configuration.GetValue<bool>("SafeActions:EnableExecution"))
            {
                telemetry.RecordGuarded501("rollback_execute");
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }

            var actionRecord = await orchestrator.GetAsync(id, ct);
            if (actionRecord is null)
                return Results.NotFound();

            var decision = throttlePolicy.Evaluate(
                actionRecord.TenantId, actionRecord.ActionType, "rollback_execute");
            if (!decision.Allowed)
            {
                telemetry.RecordExecutionThrottled(
                    actionRecord.ActionType, actionRecord.TenantId, "rollback_execute");
                logger.LogWarning(
                    "Execution throttled for {ActionType} by {TenantId}, operation {OperationKind}, retry after {RetryAfterSeconds}s",
                    actionRecord.ActionType, actionRecord.TenantId, "rollback_execute", decision.RetryAfterSeconds);
                httpContext.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString();
                return Results.Json(
                    new { reasonCode = "throttled", message = decision.Message, retryAfterSeconds = decision.RetryAfterSeconds },
                    statusCode: StatusCodes.Status429TooManyRequests,
                    contentType: "application/json");
            }

            try
            {
                var record = await orchestrator.ExecuteRollbackAsync(id, ct);
                return Results.Ok(ActionRecordResponse.From(record));
            }
            catch (PolicyDeniedException ex)
            {
                return Results.BadRequest(new { ex.ReasonCode, ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        })
        .WithName("ExecuteRollback")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status501NotImplemented)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

}
