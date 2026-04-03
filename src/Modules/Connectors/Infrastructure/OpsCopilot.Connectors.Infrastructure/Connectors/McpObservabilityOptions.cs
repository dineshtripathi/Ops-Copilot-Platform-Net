namespace OpsCopilot.Connectors.Infrastructure.Connectors;

internal sealed class McpObservabilityOptions
{
    public string Executable { get; init; } = "dotnet";

    public IReadOnlyList<string> Arguments { get; init; } =
        ["run", "--project", "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"];

    public string? WorkingDirectory { get; init; }

    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>
    /// When non-null, the HTTP (SSE) endpoint URL of the McpHost
    /// (e.g. "https://&lt;fqdn&gt;/mcp").  Takes priority over stdio child-process
    /// spawning.  Set via McpKql:ServerUrl or MCP_KQL_SERVER_URL.
    /// </summary>
    public string? ServerUrl { get; init; }
}