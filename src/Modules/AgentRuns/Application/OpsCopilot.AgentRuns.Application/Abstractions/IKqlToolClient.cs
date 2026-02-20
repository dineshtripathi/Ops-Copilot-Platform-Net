namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Port for the MCP kql_query tool. Implemented by McpHttpKqlToolClient in
/// AgentRuns.Infrastructure (calls McpHost over HTTP). Mocked in unit tests.
///
/// Constraint: ApiHost MUST NOT reference Azure.Monitor.Query directly.
/// Only McpHost executes KQL against Log Analytics.
/// </summary>
public interface IKqlToolClient
{
    Task<KqlToolResponse> ExecuteAsync(KqlToolRequest request, CancellationToken ct = default);
}
