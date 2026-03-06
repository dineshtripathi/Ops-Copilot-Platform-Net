using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Application.Extensions;
using OpsCopilot.Packs.Infrastructure.Extensions;
using OpsCopilot.Packs.Presentation.Telemetry;

namespace OpsCopilot.Packs.Presentation.Extensions;

public static class PacksPresentationExtensions
{
    public static IServiceCollection AddPacksModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPacksTelemetry, PacksTelemetry>();
        services.AddPacksApplication();
        services.AddPacksInfrastructure(configuration);
        return services;
    }
}
