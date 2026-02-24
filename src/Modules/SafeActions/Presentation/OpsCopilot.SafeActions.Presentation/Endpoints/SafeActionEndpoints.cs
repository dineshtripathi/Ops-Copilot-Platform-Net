using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Presentation.Contracts;

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

                return Results.Created(
                    $"/safe-actions/{record.ActionRecordId}",
                    ActionRecordResponse.From(record));
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
            Guid id,
            SafeActionOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var record = await orchestrator.GetAsync(id, ct);
            return record is null
                ? Results.NotFound()
                : Results.Ok(ActionRecordResponse.From(record));
        })
        .WithName("GetAction")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ── GET /safe-actions ───────────────────────────────────────
        group.MapGet("/", async (
            HttpContext ctx,
            SafeActionOrchestrator orchestrator,
            Guid? runId,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantId = ctx.Request.Headers["x-tenant-id"].ToString();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("x-tenant-id header is required.");

            var effectiveLimit = Math.Clamp(limit ?? DefaultListLimit, 1, MaxListLimit);

            IReadOnlyList<ActionRecord> records;

            if (runId.HasValue)
                records = await orchestrator.ListByRunAsync(runId.Value, ct);
            else
                records = await orchestrator.ListByTenantAsync(
                    tenantId, effectiveLimit, ct);

            return Results.Ok(records.Select(ActionRecordResponse.From));
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
            CancellationToken ct) =>
        {
            var actorId = GetActorId(ctx);

            try
            {
                var record = await orchestrator.ApproveAsync(
                    id, actorId, request.Reason, ct);
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
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/reject ──────────────────────────
        group.MapPost("/{id:guid}/reject", async (
            HttpContext ctx,
            Guid id,
            ApproveActionRequest request,
            SafeActionOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var actorId = GetActorId(ctx);

            try
            {
                var record = await orchestrator.RejectAsync(
                    id, actorId, request.Reason, ct);
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
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/execute ─────────────────────────
        group.MapPost("/{id:guid}/execute", async (
            Guid id,
            SafeActionOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                var record = await orchestrator.ExecuteAsync(id, ct);
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
        .WithName("ExecuteAction")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
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
            CancellationToken ct) =>
        {
            var actorId = GetActorId(ctx);

            try
            {
                var record = await orchestrator.ApproveRollbackAsync(
                    id, actorId, request.Reason, ct);
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
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // ── POST /safe-actions/{id}/rollback/execute ────────────────
        group.MapPost("/{id:guid}/rollback/execute", async (
            Guid id,
            SafeActionOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                var record = await orchestrator.ExecuteRollbackAsync(id, ct);
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
        .WithName("ExecuteRollback")
        .Produces<ActionRecordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    // ── Shared helpers ──────────────────────────────────────────────

    /// <summary>
    /// Extracts actor identity from the <c>x-actor-id</c> header,
    /// falling back to <c>"unknown"</c> when absent.
    /// </summary>
    private static string GetActorId(HttpContext ctx)
    {
        var value = ctx.Request.Headers["x-actor-id"].ToString();
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
