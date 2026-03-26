using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Application.Acl;
using OpsCopilot.AgentRuns.Application.Orchestration;
using OpsCopilot.AgentRuns.Application.Services;

namespace OpsCopilot.AgentRuns.Application.Extensions;

public static class AgentRunsApplicationExtensions
{
    public static IServiceCollection AddAgentRunsApplication(this IServiceCollection services)
    {
        services.AddSingleton<IRunbookAclFilter, TenantGroupRoleRunbookAclFilter>();
        services.AddSingleton<IIncidentMemoryService, NullIncidentMemoryService>();
        services.AddSingleton<IIncidentMemoryIndexer, NullIncidentMemoryIndexer>();
        services.AddScoped<TriageOrchestrator>();
        services.AddScoped<ChatOrchestrator>();
        return services;
    }
}
