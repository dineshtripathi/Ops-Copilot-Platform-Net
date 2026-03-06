using Microsoft.Extensions.DependencyInjection;

namespace OpsCopilot.Packs.Application.Extensions;

public static class PacksApplicationExtensions
{
    /// <summary>
    /// Registers Packs application-layer services (interface-only layer — no registrations needed).
    /// </summary>
    public static IServiceCollection AddPacksApplication(this IServiceCollection services)
    {
        return services;
    }
}
