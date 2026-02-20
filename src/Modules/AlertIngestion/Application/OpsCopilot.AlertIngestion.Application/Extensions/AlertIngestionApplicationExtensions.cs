using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AlertIngestion.Application.Handlers;

namespace OpsCopilot.AlertIngestion.Application.Extensions;

public static class AlertIngestionApplicationExtensions
{
    /// <summary>Registers AlertIngestion application-layer services.</summary>
    public static IServiceCollection AddAlertIngestionApplication(
        this IServiceCollection services)
    {
        services.AddScoped<IngestAlertCommandHandler>();
        return services;
    }
}
