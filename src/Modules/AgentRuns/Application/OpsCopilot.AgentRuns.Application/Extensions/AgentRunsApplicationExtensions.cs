using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Orchestration;

namespace OpsCopilot.AgentRuns.Application.Extensions;

public static class AgentRunsApplicationExtensions
{
    public static IServiceCollection AddAgentRunsApplication(this IServiceCollection services)
    {
        services.AddSingleton<IRunbookAclFilter, TenantGroupRoleRunbookAclFilter>();
        services.AddScoped<TriageOrchestrator>();
        return services;
    }
}
