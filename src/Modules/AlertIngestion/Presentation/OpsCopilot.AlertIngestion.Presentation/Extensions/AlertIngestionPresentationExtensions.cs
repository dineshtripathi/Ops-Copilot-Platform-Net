using Microsoft.Extensions.DependencyInjection;

namespace OpsCopilot.AlertIngestion.Presentation.Extensions;

public static class AlertIngestionPresentationExtensions
{
    /// <summary>
    /// Registers any Presentation-layer services for the AlertIngestion module.
    /// (Currently no scoped services — endpoints are registered via MapAlertIngestionEndpoints.)
    /// </summary>
    public static IServiceCollection AddAlertIngestionPresentation(
        this IServiceCollection services)
    {
        // Reserved for future presentation-layer registrations.
        return services;
    }
}
