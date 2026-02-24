using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.SafeActions.Application.Orchestration;

namespace OpsCopilot.SafeActions.Application.Extensions;

public static class SafeActionsApplicationExtensions
{
    public static IServiceCollection AddSafeActionsApplication(
        this IServiceCollection services)
    {
        services.AddScoped<SafeActionOrchestrator>();
        return services;
    }
}
