namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Provides a mechanism to call arbitrary MCP tools on the running McpHost process.
/// Implemented by <c>McpObservabilityQueryExecutor</c> which already owns the stdio transport.
/// </summary>
public interface IMcpToolConnector
{
    /// <summary>
    /// Calls a named MCP tool and returns the first text content block, or <c>null</c> if the tool returned no text.
    /// </summary>
    Task<string?> CallToolAsync(
        string toolName,
        Dictionary<string, object?> args,
        CancellationToken ct = default);
}
