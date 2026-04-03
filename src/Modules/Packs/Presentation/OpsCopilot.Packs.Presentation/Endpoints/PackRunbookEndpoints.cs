using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Packs.Application.Abstractions;

namespace OpsCopilot.Packs.Presentation.Endpoints;

public static class PackRunbookEndpoints
{
    public static IEndpointRouteBuilder MapPackRunbookEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Serves runbook markdown files from across all loaded packs.
        // Route parameter is the filename only — no subdirectories.
        app.MapGet("/runbooks/{runbookName}", GetRunbook)
           .WithTags("Runbooks")
           .WithName("GetRunbook");

        return app;
    }

    // ── GET /runbooks/{runbookName} ──────────────────────────────────
    private static async Task<IResult> GetRunbook(
        string runbookName,
        IPackRunbookReader reader,
        CancellationToken ct)
    {
        var content = await reader.ReadAsync(runbookName, ct);

        if (content is null)
            return Results.NotFound(new { error = $"Runbook '{runbookName}' not found." });

        return Results.Text(content, "text/plain; charset=utf-8");
    }
}
