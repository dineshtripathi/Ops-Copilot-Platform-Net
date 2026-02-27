using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Services;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven tool allowlist. Denies tools not in the allowlist.
/// An empty allowlist means all tools are allowed.
/// Uses <see cref="ITenantAwareGovernanceOptionsResolver"/> for tenant-aware resolution.
/// </summary>
public sealed class DefaultToolAllowlistPolicy : IToolAllowlistPolicy
{
    private readonly ITenantAwareGovernanceOptionsResolver _resolver;

    public DefaultToolAllowlistPolicy(ITenantAwareGovernanceOptionsResolver resolver)
    {
        _resolver = resolver;
    }

    public PolicyDecision CanUseTool(string tenantId, string toolName)
    {
        var resolved = _resolver.Resolve(tenantId);

        // Empty allowlist = everything is allowed
        if (resolved.AllowedTools.Count == 0)
            return PolicyDecision.Allow();

        if (resolved.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return PolicyDecision.Allow();

        return PolicyDecision.Deny("TOOL_DENIED",
            $"Tool '{toolName}' is not in the allowlist for tenant '{tenantId}'.");
    }
}
