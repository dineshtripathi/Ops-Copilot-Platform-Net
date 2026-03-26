namespace OpsCopilot.Connectors.Infrastructure.Connectors;

internal sealed class McpObservabilityOptions
{
    public string Executable { get; init; } = "dotnet";

    public IReadOnlyList<string> Arguments { get; init; } =
        ["run", "--project", "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"];

    public string? WorkingDirectory { get; init; }

    public int TimeoutSeconds { get; init; } = 90;
}