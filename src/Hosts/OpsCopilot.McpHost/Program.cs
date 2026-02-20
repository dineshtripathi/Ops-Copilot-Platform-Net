using Azure.Identity;
using Azure.Monitor.Query;
using OpsCopilot.McpHost.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// OpsCopilot.McpHost — MCP hard-boundary host
//
// Contract: ApiHost is the ONLY caller of this host.
//           This host is the ONLY component allowed to call Azure Monitor.
// Exposes:  POST /mcp/tools/kql_query
// ─────────────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// ── Azure Monitor Query client ────────────────────────────────────────────────
// Uses system-assigned Managed Identity in Container Apps (DefaultAzureCredential).
// For local dev: az login or AZURE_CLIENT_ID/AZURE_CLIENT_SECRET env vars.
builder.Services.AddSingleton(_ => new LogsQueryClient(new DefaultAzureCredential()));

// ── KQL handler ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<KqlQueryHandler>();

// ── Observability ─────────────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

// ── Health probe ─────────────────────────────────────────────────────────────
app.MapGet("/healthz", () => Results.Ok("healthy"))
   .WithName("Health")
   .ExcludeFromDescription();

// ── MCP Tool: kql_query ──────────────────────────────────────────────────────
// Called exclusively by ApiHost (AgentRuns.Infrastructure → McpHttpKqlToolClient).
app.MapPost("/mcp/tools/kql_query", async (
    KqlQueryRequest  request,
    KqlQueryHandler  handler,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkspaceIdOrName))
        return Results.BadRequest("WorkspaceIdOrName is required.");
    if (string.IsNullOrWhiteSpace(request.Kql))
        return Results.BadRequest("Kql is required.");
    if (string.IsNullOrWhiteSpace(request.TimespanIso8601))
        return Results.BadRequest("TimespanIso8601 is required.");

    var response = await handler.ExecuteAsync(request, ct);
    return Results.Ok(response);
})
.WithName("KqlQuery")
.WithTags("MCP Tools")
.Accepts<KqlQueryRequest>("application/json")
.Produces<KqlQueryResponse>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest);

app.Run();

