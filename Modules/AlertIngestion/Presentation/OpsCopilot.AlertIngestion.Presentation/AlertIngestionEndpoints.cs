using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.AgentRuns.Application;
using OpsCopilot.BuildingBlocks.Contracts;

namespace OpsCopilot.AlertIngestion.Presentation;

public static class AlertIngestionEndpointRegistration
{
    public static IEndpointRouteBuilder MapAlertIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ingest/alert", async (AlertPayload payload, TriageService triageService, CancellationToken cancellationToken) =>
        {
            var response = await triageService.RunAsync(new TriageRequest { Alert = payload }, cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        })
        .WithName("IngestAlert")
        .WithTags("AlertIngestion");

        return endpoints;
    }
}