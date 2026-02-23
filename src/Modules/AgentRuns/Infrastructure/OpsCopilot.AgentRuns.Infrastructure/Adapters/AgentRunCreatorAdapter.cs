using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.BuildingBlocks.Contracts.AgentRuns;

namespace OpsCopilot.AgentRuns.Infrastructure.Adapters;

/// <summary>
/// Adapts the full <see cref="IAgentRunRepository"/> to the narrow
/// <see cref="IAgentRunCreator"/> contract consumed by other modules.
/// </summary>
internal sealed class AgentRunCreatorAdapter : IAgentRunCreator
{
    private readonly IAgentRunRepository _repository;

    public AgentRunCreatorAdapter(IAgentRunRepository repository)
        => _repository = repository;

    public async Task<Guid> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var run = await _repository.CreateRunAsync(tenantId, alertFingerprint, sessionId, ct);
        return run.RunId;
    }
}
