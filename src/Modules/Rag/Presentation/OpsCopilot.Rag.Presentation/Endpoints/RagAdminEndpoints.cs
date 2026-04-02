using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsCopilot.Rag.Application;

namespace OpsCopilot.Rag.Presentation.Endpoints;

/// <summary>
/// RAG administration endpoints. Slice 183.
/// </summary>
public static class RagAdminEndpoints
{
    public static IEndpointRouteBuilder MapRagAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rag")
                       .WithTags("RAG Administration");

        // POST /rag/runbooks/reindex?tenantId={tenantId}
        // Triggers a full re-ingest of runbooks from disk into the vector store.
        group.MapPost("/runbooks/reindex", ReindexRunbooks);

        return app;
    }

    private static async Task<IResult> ReindexRunbooks(
        IRunbookReindexService reindexService,
        string                 tenantId,
        CancellationToken      ct)
    {
        var count = await reindexService.ReindexAllAsync(tenantId, ct);
        return Results.Ok(new { indexedCount = count, tenantId });
    }
}
