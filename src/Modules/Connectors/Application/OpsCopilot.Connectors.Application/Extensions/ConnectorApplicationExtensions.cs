using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Application.Services;

namespace OpsCopilot.Connectors.Application.Extensions;

public static class ConnectorApplicationExtensions
{
    /// <summary>
    /// Registers the <see cref="IConnectorRegistry"/> and its application-level
    /// dependencies. Concrete connector implementations are added by the
    /// Infrastructure layer.
    /// </summary>
    public static IServiceCollection AddConnectorsApplication(this IServiceCollection services)
    {
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
        return services;
    }
}
