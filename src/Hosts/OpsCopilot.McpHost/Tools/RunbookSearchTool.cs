using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpsCopilot.Rag.Application;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// Exposes the "runbook_search" MCP tool.
///
/// Discovered automatically by <c>WithToolsFromAssembly()</c>
/// via the <see cref="McpServerToolTypeAttribute"/> marker.
///
/// <see cref="IRunbookRetrievalService"/> is resolved from the host's service
/// container per-invocation — registered by <c>AddRagInfrastructure()</c>.
/// </summary>
[McpServerToolType]
public sealed class RunbookSearchTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Search the runbook knowledge base for relevant operational procedures.
    /// Returns ranked hits with title, snippet, and relevance score.
    ///
    /// On success  → ok=true,  hits populated, error=null.
    /// On failure  → ok=false, hits=[], error contains message.
    /// </summary>
    [McpServerTool(Name = "runbook_search")]
    [Description(
        "Search the operational runbook knowledge base using keywords. " +
        "Returns ranked results with title, snippet, and relevance score. " +
        "Use this to find troubleshooting procedures, remediation steps, " +
        "and operational guidance for alerts and incidents.")]
    public static async Task<string> ExecuteAsync(
        // Injected from DI — registered as singleton via AddRagInfrastructure
        IRunbookRetrievalService retrievalService,
        ILoggerFactory loggerFactory,

        // MCP tool parameters — appear in the JSON input schema
        [Description(
            "Keywords to search for in the runbook knowledge base, e.g. " +
            "'high cpu troubleshooting' or 'pod crashloop kubernetes'.")]
        string query,

        [Description(
            "Maximum number of results to return. Defaults to 5. " +
            "Range: 1–20.")]
        int maxResults = 5,

        CancellationToken cancellationToken = default)
    {
        var logger = loggerFactory.CreateLogger<RunbookSearchTool>();

        try
        {
            logger.LogInformation("runbook_search invoked — query={Query}, maxResults={MaxResults}",
                query, maxResults);

            // Clamp maxResults to a sensible range
            maxResults = Math.Clamp(maxResults, 1, 20);

            var searchQuery = new RunbookSearchQuery(query, maxResults);
            var results = await retrievalService.SearchAsync(searchQuery, cancellationToken);

            var hits = results.Select(r => new
            {
                r.RunbookId,
                r.Title,
                r.Snippet,
                r.Score
            }).ToArray();

            var envelope = new
            {
                ok = true,
                query,
                hitCount = hits.Length,
                hits,
                error = (object?)null
            };

            logger.LogInformation("runbook_search completed — {HitCount} hit(s) for query={Query}",
                hits.Length, query);

            return JsonSerializer.Serialize(envelope, JsonOpts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "runbook_search failed — query={Query}", query);

            var envelope = new
            {
                ok = false,
                query,
                hitCount = 0,
                hits = Array.Empty<object>(),
                error = new { message = ex.Message, type = ex.GetType().Name }
            };

            return JsonSerializer.Serialize(envelope, JsonOpts);
        }
    }
}
