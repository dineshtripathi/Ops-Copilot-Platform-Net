namespace OpsCopilot.AgentRuns.Infrastructure.McpClient;

/// <summary>
/// Configuration for the McpHost child process that the
/// <see cref="McpStdioKqlToolClient"/> starts via stdio transport.
///
/// Controlled by environment variables (read in AgentRunsInfrastructureExtensions):
///   MCP_KQL_SERVER_COMMAND  — full flat command string parsed into executable + args,
///                             e.g. "dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"
///                             or   "dotnet /app/OpsCopilot.McpHost.dll"
///   MCP_KQL_SERVER_WORKDIR  — working directory for the child process;
///                             auto-discovered from solution root when omitted in dev.
///   MCP_KQL_TIMEOUT_SECONDS — per-call timeout in seconds. Default: 30.
/// </summary>
public sealed class McpKqlServerOptions
{
    // ── Defaults (local development) ─────────────────────────────────────────
    // 'dotnet run' from the solution root.  Override with the env vars above
    // for production (e.g. "dotnet /app/OpsCopilot.McpHost.dll").

    /// <summary>The executable to launch.</summary>
    public string Executable { get; init; } = "dotnet";

    /// <summary>
    /// Arguments passed to <see cref="Executable"/>.
    /// Defaults to a 'dotnet run' of the McpHost project relative to the
    /// solution root.  Populated from <c>MCP_KQL_SERVER_COMMAND</c> when set.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } =
        ["run", "--project", "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"];

    /// <summary>
    /// Working directory for the child process.
    /// <c>null</c> → auto-discovered by walking up from
    /// <see cref="AppContext.BaseDirectory"/> to the first directory that
    /// contains a <c>*.sln</c> file (works in both local dev and test runs).
    /// Set to an absolute path in production container images where the
    /// command is an absolute path and no .sln file exists.
    /// </summary>
    public string? WorkingDirectory { get; init; } = null;

    /// <summary>Per-call timeout in seconds.  Default: 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
