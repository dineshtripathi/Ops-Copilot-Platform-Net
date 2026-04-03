using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Application.Extensions;
using OpsCopilot.Connectors.Infrastructure.Connectors;
using OpsCopilot.Connectors.Infrastructure.Services;

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

        // Credential provider — reads connector secrets from IConfiguration / Azure Key Vault.
        services.AddSingleton<IConnectorCredentialProvider, KeyVaultConnectorCredentialProvider>();

        // Health check — verifies that a credential is present for a given connector.
        services.AddSingleton<IConnectorHealthCheck, ConnectorHealthCheckRunner>();

        // Tenant credential manager — §6.18; override with Key Vault when configured.
        var vaultUri = configuration?["KeyVault:VaultUri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
            services.AddSingleton<ITenantCredentialManager>(
                new KeyVaultTenantCredentialManager(
                    new SecretClient(new Uri(vaultUri), new DefaultAzureCredential())));
        else
            services.AddSingleton<ITenantCredentialManager, NullTenantCredentialManager>();

        // Infrastructure — concrete connectors (config-driven, explicit, no reflection)
        services.AddSingleton<IObservabilityConnector, AzureMonitorObservabilityConnector>();

        // Runbook connector: switch to the Git-backed implementation when a repository URL is
        // provided; otherwise fall back to the in-memory connector (no external dependencies).
        if (!string.IsNullOrWhiteSpace(configuration?[GitRunbookConnector.RepositoryUrlConfigKey]))
            services.AddSingleton<IRunbookConnector, GitRunbookConnector>();
        else
            services.AddSingleton<IRunbookConnector, InMemoryRunbookConnector>();

        // Resolve ARM action types from configuration once at startup; fall back to defaults.
        var armActionTypes = configuration?["Connectors:ArmTarget:SupportedActions"]
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ArmResourceTargetConnector.DefaultActionTypes;
        services.AddSingleton<IActionTargetConnector>(sp =>
            new ArmResourceTargetConnector(
                armActionTypes,
                sp.GetRequiredService<ILogger<ArmResourceTargetConnector>>()));

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
        var serverUrl = configuration?["McpKql:ServerUrl"]
            ?? configuration?["MCP_KQL_SERVER_URL"];

        if (!string.IsNullOrWhiteSpace(cmdStr))
        {
            var tokens = cmdStr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new McpObservabilityOptions
            {
                Executable = tokens[0],
                Arguments = tokens[1..],
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
                TimeoutSeconds = timeout,
                ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl,
            };
        }

        return new McpObservabilityOptions
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            TimeoutSeconds = timeout,
            ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl,
        };
    }
}
