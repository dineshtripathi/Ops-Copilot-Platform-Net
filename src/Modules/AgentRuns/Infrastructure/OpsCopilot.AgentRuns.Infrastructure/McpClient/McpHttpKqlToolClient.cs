using System.Net.Http.Json;
using System.Text.Json;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.McpClient;

/// <summary>
/// Translates IKqlToolClient calls into HTTP POST /mcp/tools/kql_query requests
/// against McpHost. Uses typed HttpClient registered via AddHttpClient in DI.
///
/// Configuration required:
///   MCP_HOST_BASEURL — base URL of the McpHost container app,
///   e.g. https://ca-opscopilot-mcphost-dev.*.uksouth.azurecontainerapps.io
///
/// Constraint: This is the ONLY class in ApiHost's process that talks to McpHost.
/// ApiHost never references Azure.Monitor.Query.
/// </summary>
public sealed class McpHttpKqlToolClient : IKqlToolClient
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public McpHttpKqlToolClient(HttpClient http) => _http = http;

    public async Task<KqlToolResponse> ExecuteAsync(
        KqlToolRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/mcp/tools/kql_query", request, JsonOpts, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<KqlToolResponse>(JsonOpts, ct)
               ?? throw new InvalidOperationException("Empty response from MCP kql_query.");
    }
}
