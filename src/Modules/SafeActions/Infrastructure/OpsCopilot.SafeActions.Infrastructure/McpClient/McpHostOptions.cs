namespace OpsCopilot.SafeActions.Infrastructure.McpClient;

/// <summary>
/// Configuration for the OpsCopilot.McpHost child process launched by
/// <see cref="SafeActionsMcpHostClient"/>.
///
/// Config keys (see SafeActionsInfrastructureExtensions.BuildMcpHostOptions):
///   McpKql:ServerCommand   (env: MCP_KQL_SERVER_COMMAND)
///   McpKql:WorkDir         (env: MCP_KQL_SERVER_WORKDIR)
///   McpKql:TimeoutSeconds  (env: MCP_KQL_TIMEOUT_SECONDS)
/// </summary>
internal sealed class McpHostOptions
{
    /// <summary>The executable to launch — defaults to <c>dotnet</c>.</summary>
    public string Executable { get; init; } = "dotnet";

    /// <summary>
    /// Arguments passed to <see cref="Executable"/>.
    /// Defaults to <c>dotnet run</c> of the McpHost project from the solution root.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } =
        ["run", "--project", "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"];

    /// <summary>
    /// Working directory for the child process.
    /// <c>null</c> → auto-discovered by walking up from <see cref="AppContext.BaseDirectory"/>
    /// until a <c>*.sln</c> file is found.
    /// </summary>
    public string? WorkingDirectory { get; init; } = null;

    /// <summary>Per-call timeout in seconds. Default: 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
