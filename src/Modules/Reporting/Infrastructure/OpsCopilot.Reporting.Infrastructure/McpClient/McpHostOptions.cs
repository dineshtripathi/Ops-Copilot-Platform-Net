namespace OpsCopilot.Reporting.Infrastructure.McpClient;

/// <summary>
/// Configuration options for the OpsCopilot.McpHost child process
/// used by the Reporting module.
///
/// Defaults allow starting McpHost via <c>dotnet run</c> from the solution root,
/// which works in local development without extra configuration.
/// In production containers the executable is a published binary — configure
/// via <c>McpKql:ServerCommand</c> or the <c>MCP_KQL_SERVER_COMMAND</c> environment variable.
/// </summary>
internal sealed class McpHostOptions
{
    /// <summary>The executable to launch (e.g. "dotnet" or the published binary path).</summary>
    public string Executable { get; init; } = "dotnet";

    /// <summary>Arguments passed to the executable when starting the child process.</summary>
    public string[] Arguments { get; init; } =
    [
        "run",
        "--project",
        "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj",
    ];

    /// <summary>
    /// Working directory for the child process.
    /// When null the Reporting module will attempt to discover the solution root at runtime.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Timeout per MCP tool call in seconds. Defaults to 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
