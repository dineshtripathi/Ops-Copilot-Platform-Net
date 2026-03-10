using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.Tenancy.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Infrastructure.Routing;

internal sealed class TenantConfigModelRoutingPolicy(ITenantConfigStore store) : IModelRoutingPolicy
{
    private const string RoutingKey = "model:triage";

    public async Task<ModelDescriptor> SelectModelAsync(string tenantId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(tenantId, out var tid))
            return new ModelDescriptor("default");

        var entries = await store.GetAsync(tid, ct);
        var value   = entries.FirstOrDefault(e => e.Key == RoutingKey)?.Value;
        return new ModelDescriptor(value ?? "default");
    }
}
