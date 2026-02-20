using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Application.Handlers;
using OpsCopilot.AlertIngestion.Presentation.Contracts;

namespace OpsCopilot.AlertIngestion.Presentation.Endpoints;

public static class AlertIngestionEndpoints
{
    public static IEndpointRouteBuilder MapAlertIngestionEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /ingest/alert
        // Required header : x-tenant-id
        // Optional headers: x-subscription-id, x-correlation-id
        // Body (JSON)     : IngestAlertRequest
        app.MapPost("/ingest/alert", async (
            HttpContext              httpContext,
            IngestAlertRequest       request,
            IngestAlertCommandHandler handler,
            CancellationToken        ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest("Missing required header: x-tenant-id");

            if (string.IsNullOrWhiteSpace(request.Payload))
                return Results.BadRequest("Payload must not be empty.");

            // Optional tracing headers are available for future use.
            // var subscriptionId = httpContext.Request.Headers["x-subscription-id"].FirstOrDefault();
            // var correlationId  = httpContext.Request.Headers["x-correlation-id"].FirstOrDefault();

            var command = new IngestAlertCommand(tenantId, request.Payload);
            var result  = await handler.HandleAsync(command, ct);

            return Results.Ok(new IngestAlertResponse(result.RunId, result.Fingerprint));
        })
        .WithName("PostIngestAlert")
        .WithTags("AlertIngestion")
        .Accepts<IngestAlertRequest>("application/json")
        .Produces<IngestAlertResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
