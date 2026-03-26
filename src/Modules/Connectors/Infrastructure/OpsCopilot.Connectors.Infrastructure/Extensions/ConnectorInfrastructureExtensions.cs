using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Application.Extensions;
using OpsCopilot.Connectors.Infrastructure.Connectors;

namespace OpsCopilot.Connectors.Infrastructure.Extensions;

public static class ConnectorInfrastructureExtensions
{
    /// <summary>
    /// Registers all Connectors module services — application layer (registry)
    /// plus concrete connector implementations. This is the single entry-point
    /// that ApiHost calls; no Presentation layer is needed because Connectors
    /// is an internal module with no HTTP endpoints.
    /// </summary>
    public static IServiceCollection AddConnectorsModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Application layer — registry
        services.AddConnectorsApplication();

        // Infrastructure — concrete connectors (config-driven, explicit, no reflection)
        services.AddSingleton<IObservabilityConnector, AzureMonitorObservabilityConnector>();
        services.AddSingleton<IRunbookConnector, InMemoryRunbookConnector>();
        services.AddSingleton<IActionTargetConnector, StaticActionTargetConnector>();

        // Observability query executor (MCP stdio to McpHost; no direct Azure Monitor SDK calls).
        // Registered as a concrete singleton first so both interface registrations share one instance.
        services.AddSingleton(BuildMcpOptions(configuration));
        services.AddSingleton<McpObservabilityQueryExecutor>();
        services.AddSingleton<IObservabilityQueryExecutor>(
            sp => sp.GetRequiredService<McpObservabilityQueryExecutor>());
        services.AddSingleton<IMcpToolConnector>(
            sp => sp.GetRequiredService<McpObservabilityQueryExecutor>());

        // Resource discovery — calls discover_observability_resources MCP tool.
        services.AddSingleton<IObservabilityResourceDiscovery, McpObservabilityResourceDiscovery>();

        return services;
    }

    private static McpObservabilityOptions BuildMcpOptions(IConfiguration? configuration)
    {
        var cmdStr = configuration?["McpKql:ServerCommand"]
            ?? configuration?["MCP_KQL_SERVER_COMMAND"];
        var workDir = configuration?["McpKql:WorkDir"]
            ?? configuration?["MCP_KQL_SERVER_WORKDIR"];
        var timeoutStr = configuration?["McpKql:TimeoutSeconds"]
            ?? configuration?["MCP_KQL_TIMEOUT_SECONDS"];
        var timeout = int.TryParse(timeoutStr, out var parsedTimeout) ? parsedTimeout : 90;

        if (!string.IsNullOrWhiteSpace(cmdStr))
        {
            var tokens = cmdStr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new McpObservabilityOptions
            {
                Executable = tokens[0],
                Arguments = tokens[1..],
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
                TimeoutSeconds = timeout,
            };
        }

        return new McpObservabilityOptions
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            TimeoutSeconds = timeout,
        };
    }
}
