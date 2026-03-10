namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>Selects the appropriate LLM model descriptor for a given tenant.</summary>
public interface IModelRoutingPolicy
{
    Task<ModelDescriptor> SelectModelAsync(string tenantId, CancellationToken ct = default);
}
