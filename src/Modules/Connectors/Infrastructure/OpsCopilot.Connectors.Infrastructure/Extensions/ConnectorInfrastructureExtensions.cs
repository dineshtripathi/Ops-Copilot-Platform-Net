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
    public static IServiceCollection AddConnectorsModule(this IServiceCollection services)
    {
        // Application layer — registry
        services.AddConnectorsApplication();

        // Infrastructure — concrete connectors (config-driven, explicit, no reflection)
        services.AddSingleton<IObservabilityConnector, AzureMonitorObservabilityConnector>();
        services.AddSingleton<IRunbookConnector, InMemoryRunbookConnector>();
        services.AddSingleton<IActionTargetConnector, StaticActionTargetConnector>();

        return services;
    }
}
