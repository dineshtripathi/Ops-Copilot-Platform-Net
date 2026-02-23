using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AlertIngestion.Application.Extensions;

namespace OpsCopilot.AlertIngestion.Presentation.Extensions;

public static class AlertIngestionPresentationExtensions
{
    /// <summary>
    /// Registers all AlertIngestion module services: Application and Presentation layers.
    /// Hosts call only this method — inner layers are hidden behind this facade.
    /// </summary>
    public static IServiceCollection AddAlertIngestionModule(
        this IServiceCollection services)
    {
        services.AddAlertIngestionApplication();
        // Reserved for future presentation-layer registrations.
        return services;
    }
}
