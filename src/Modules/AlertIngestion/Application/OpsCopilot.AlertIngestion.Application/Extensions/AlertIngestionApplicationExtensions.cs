using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Application.Handlers;
using OpsCopilot.AlertIngestion.Application.Normalizers;
using OpsCopilot.AlertIngestion.Application.Services;

namespace OpsCopilot.AlertIngestion.Application.Extensions;

public static class AlertIngestionApplicationExtensions
{
    /// <summary>Registers AlertIngestion application-layer services.</summary>
    public static IServiceCollection AddAlertIngestionApplication(
        this IServiceCollection services)
    {
        // Provider normalizers
        services.AddSingleton<IAlertNormalizer, AzureMonitorAlertNormalizer>();
        services.AddSingleton<IAlertNormalizer, DatadogAlertNormalizer>();
        services.AddSingleton<IAlertNormalizer, GenericAlertNormalizer>();

        // Router (depends on IEnumerable<IAlertNormalizer>)
        services.AddSingleton<AlertNormalizerRouter>();

        // Command handler
        services.AddScoped<IngestAlertCommandHandler>();

        return services;
    }
}
