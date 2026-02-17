using System.Collections.Concurrent;
using OpsCopilot.AgentRuns.Application;
using OpsCopilot.AgentRuns.Domain;

namespace OpsCopilot.AgentRuns.Infrastructure.Persistence;

public sealed class InMemoryAgentRunRepository : IAgentRunRepository
{
    private readonly ConcurrentDictionary<Guid, AgentRun> _store = new();

    public Task SaveAsync(AgentRun agentRun, CancellationToken cancellationToken)
    {
        _store[agentRun.Id] = agentRun;
        return Task.CompletedTask;
    }

    public Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var run);
        return Task.FromResult(run);
    }
}