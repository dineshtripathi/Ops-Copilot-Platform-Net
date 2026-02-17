using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.AgentRuns.Application;
using OpsCopilot.BuildingBlocks.Contracts;

namespace OpsCopilot.AgentRuns.Presentation;

public static class AgentRunEndpointRegistration
{
    public static IEndpointRouteBuilder MapAgentRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/agent/triage", async (TriageRequest request, TriageService triageService, CancellationToken cancellationToken) =>
        {
            var response = await triageService.RunAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        })
        .WithName("TriageAgent")
        .WithTags("AgentRuns");

        return endpoints;
    }
}