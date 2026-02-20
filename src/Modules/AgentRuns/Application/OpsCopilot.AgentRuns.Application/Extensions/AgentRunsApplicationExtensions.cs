using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Orchestration;

namespace OpsCopilot.AgentRuns.Application.Extensions;

public static class AgentRunsApplicationExtensions
{
    public static IServiceCollection AddAgentRunsApplication(this IServiceCollection services)
    {
        services.AddScoped<TriageOrchestrator>();
        return services;
    }
}
