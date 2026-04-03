namespace OpsCopilot.Reporting.Infrastructure.McpClient;

/// <summary>
/// Abstraction over <see cref="ReportingMcpHostClient"/> so that consumers
/// can be unit-tested without spawning a real child process.
/// </summary>
internal interface IReportingMcpHostClient
{
    Task<string> CallToolAsync(
        string                      toolName,
        Dictionary<string, object?> args,
        CancellationToken           ct);
}
