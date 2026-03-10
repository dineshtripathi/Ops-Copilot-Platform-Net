namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Fallback routing policy — returns a static "default" model descriptor.
/// Registered automatically when no real IModelRoutingPolicy is wired.
/// </summary>
internal sealed class NullModelRoutingPolicy : IModelRoutingPolicy
{
    public Task<ModelDescriptor> SelectModelAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(new ModelDescriptor("default"));
}
