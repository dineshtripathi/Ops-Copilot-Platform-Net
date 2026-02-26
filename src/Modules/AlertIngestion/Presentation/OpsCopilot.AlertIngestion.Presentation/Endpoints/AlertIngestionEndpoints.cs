using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.AlertIngestion.Application.Commands;
using OpsCopilot.AlertIngestion.Application.Handlers;
using OpsCopilot.AlertIngestion.Application.Services;
using OpsCopilot.AlertIngestion.Presentation.Contracts;

namespace OpsCopilot.AlertIngestion.Presentation.Endpoints;

public static class AlertIngestionEndpoints
{
    public static IEndpointRouteBuilder MapAlertIngestionEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /ingest/alert
        // Required header : x-tenant-id
        // Required body   : IngestAlertRequest { Provider, Payload }
        app.MapPost("/ingest/alert", async (
            HttpContext              httpContext,
            IngestAlertRequest       request,
            IngestAlertCommandHandler handler,
            AlertNormalizerRouter    router,
            CancellationToken        ct) =>
        {
            var tenantId = httpContext.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantId))
                return Results.BadRequest(new IngestAlertErrorResponse(
                    "missing_tenant", "Missing required header: x-tenant-id"));

            // Validate payload
            var payloadResult = AlertValidationService.ValidatePayload(request.Payload);
            if (!payloadResult.IsValid)
                return Results.BadRequest(new IngestAlertErrorResponse(
                    payloadResult.ReasonCode!, payloadResult.Message!));

            // Validate provider
            var providerResult = AlertValidationService.ValidateProvider(request.Provider, router);
            if (!providerResult.IsValid)
                return Results.BadRequest(new IngestAlertErrorResponse(
                    providerResult.ReasonCode!, providerResult.Message!));

            var command = new IngestAlertCommand(tenantId, request.Provider, request.Payload);
            var result  = await handler.HandleAsync(command, ct);

            return Results.Ok(new IngestAlertResponse(result.RunId, result.Fingerprint));
        })
        .WithName("PostIngestAlert")
        .WithTags("AlertIngestion")
        .Accepts<IngestAlertRequest>("application/json")
        .Produces<IngestAlertResponse>(StatusCodes.Status200OK)
        .Produces<IngestAlertErrorResponse>(StatusCodes.Status400BadRequest);

        return app;
    }
}
