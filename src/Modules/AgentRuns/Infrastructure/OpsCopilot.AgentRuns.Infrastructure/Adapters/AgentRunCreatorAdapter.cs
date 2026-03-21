using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Domain.Models;
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

    public async Task<Guid> CreateRunAsync(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        AlertRunContext? context = null,
        CancellationToken ct = default)
    {
        var runContext = context is null
            ? null
            : new RunContext(
                AlertProvider: context.AlertProvider,
                AlertSourceType: context.AlertSourceType,
                IsExceptionSignal: context.IsExceptionSignal,
                AzureSubscriptionId: context.AzureSubscriptionId,
                AzureResourceGroup: context.AzureResourceGroup,
                AzureResourceId: context.AzureResourceId,
                AzureApplication: context.AzureApplication,
                AzureWorkspaceId: context.AzureWorkspaceId);

        var run = await _repository.CreateRunAsync(tenantId, alertFingerprint, sessionId, runContext, ct);
        return run.RunId;
    }
}
