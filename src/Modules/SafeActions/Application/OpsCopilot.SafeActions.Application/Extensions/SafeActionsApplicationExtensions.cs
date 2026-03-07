using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.BuildingBlocks.Contracts.SafeActions;
using OpsCopilot.SafeActions.Application.Orchestration;

namespace OpsCopilot.SafeActions.Application.Extensions;

public static class SafeActionsApplicationExtensions
{
    public static IServiceCollection AddSafeActionsApplication(
        this IServiceCollection services)
    {
        services.AddScoped<SafeActionOrchestrator>();
        services.AddScoped<ISafeActionProposalService, SafeActionProposalServiceAdapter>();
        return services;
    }
}
