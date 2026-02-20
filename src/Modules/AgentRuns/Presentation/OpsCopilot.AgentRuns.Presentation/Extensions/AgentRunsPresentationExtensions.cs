using Microsoft.Extensions.DependencyInjection;

namespace OpsCopilot.AgentRuns.Presentation.Extensions;

public static class AgentRunsPresentationExtensions
{
    /// <summary>
    /// Registers any Presentation-layer services for the AgentRuns module.
    /// (Currently no scoped services — endpoints are registered via MapAgentRunEndpoints.)
    /// </summary>
    public static IServiceCollection AddAgentRunsPresentation(
        this IServiceCollection services)
    {
        // Reserved for future presentation-layer registrations (e.g. request validators).
        return services;
    }
}
