using OpsCopilot.AgentRuns.Domain;

namespace OpsCopilot.AgentRuns.Application;

public interface IAgentRunRepository
{
    Task SaveAsync(AgentRun agentRun, CancellationToken cancellationToken);
    Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken);
}