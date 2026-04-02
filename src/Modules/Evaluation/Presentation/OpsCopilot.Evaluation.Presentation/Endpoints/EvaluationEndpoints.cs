using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Evaluation.Application.Services;
using OpsCopilot.Evaluation.Domain.Models;

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

        group.MapPost("/run/async", (EvaluationRunner runner, EvaluationRunStore store) =>
        {
            var runId = Guid.NewGuid();
            store.StartRun(runId);

            _ = Task.Run(async () =>
            {
                try
                {
                    var summary = await runner.RunAsync();
                    var corrected = summary with { RunId = runId };
                    store.CompleteRun(runId, corrected);
                }
                catch
                {
                    store.FailRun(runId);
                }
            });

            return Results.Accepted($"/evaluation/run/{runId}", new { runId });
        })
        .WithName("RunEvaluationAsync")
        .Produces(202);

        group.MapGet("/run/{id:guid}", (Guid id, EvaluationRunStore store) =>
        {
            var run = store.GetRun(id);
            if (run is null) return Results.NotFound();
            if (run.Status == EvaluationRunStatus.Running) return Results.Accepted(value: run);
            return Results.Ok(run);
        })
        .WithName("GetEvaluationRun")
        .Produces(200)
        .Produces(202)
        .Produces(404);

        group.MapGet("/scenarios", (EvaluationScenarioCatalog catalog) =>
        {
            var metadata = catalog.GetAllScenarios();
            return Results.Ok(metadata);
        })
        .WithName("ListEvaluationScenarios")
        .Produces(200);

        return app;
    }
}
