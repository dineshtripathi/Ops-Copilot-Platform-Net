using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Evaluation.Application.Services;

namespace OpsCopilot.Evaluation.Presentation.Endpoints;

public static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/evaluation").WithTags("Evaluation");

        group.MapGet("/run", (EvaluationRunner runner) =>
        {
            var summary = runner.Run();
            return Results.Ok(summary);
        })
        .WithName("RunEvaluation")
        .Produces(200);

        group.MapGet("/scenarios", (EvaluationScenarioCatalog catalog) =>
        {
            var metadata = catalog.GetMetadata();
            return Results.Ok(metadata);
        })
        .WithName("ListEvaluationScenarios")
        .Produces(200);

        return app;
    }
}
