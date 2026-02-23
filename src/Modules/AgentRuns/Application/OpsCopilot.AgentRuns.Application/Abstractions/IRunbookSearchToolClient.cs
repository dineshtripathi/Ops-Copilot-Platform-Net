namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Port for the MCP runbook_search tool. Implemented by McpStdioRunbookToolClient
/// in AgentRuns.Infrastructure (calls McpHost over stdio). Mocked in unit tests.
///
/// Constraint: ApiHost MUST NOT reference Rag infrastructure directly.
/// Only McpHost executes runbook retrieval.
/// </summary>
public interface IRunbookSearchToolClient
{
    Task<RunbookSearchToolResponse> ExecuteAsync(RunbookSearchToolRequest request, CancellationToken ct = default);
}
