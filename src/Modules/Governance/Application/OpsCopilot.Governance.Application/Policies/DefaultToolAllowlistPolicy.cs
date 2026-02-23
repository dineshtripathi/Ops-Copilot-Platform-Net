using Microsoft.Extensions.Options;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Configuration;

namespace OpsCopilot.Governance.Application.Policies;

/// <summary>
/// Config-driven tool allowlist. Denies tools not in the allowlist.
/// An empty allowlist means all tools are allowed.
/// </summary>
public sealed class DefaultToolAllowlistPolicy : IToolAllowlistPolicy
{
    private readonly GovernanceOptions _options;

    public DefaultToolAllowlistPolicy(IOptions<GovernanceOptions> options)
    {
        _options = options.Value;
    }

    public PolicyDecision CanUseTool(string tenantId, string toolName)
    {
        var allowedTools = ResolveAllowedTools(tenantId);

        // Empty allowlist = everything is allowed
        if (allowedTools.Count == 0)
            return PolicyDecision.Allow();

        if (allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return PolicyDecision.Allow();

        return PolicyDecision.Deny("TOOL_DENIED",
            $"Tool '{toolName}' is not in the allowlist for tenant '{tenantId}'.");
    }

    private List<string> ResolveAllowedTools(string tenantId)
    {
        if (_options.TenantOverrides.TryGetValue(tenantId, out var tenantOverride)
            && tenantOverride.AllowedTools is not null)
        {
            return tenantOverride.AllowedTools;
        }

        return _options.Defaults.AllowedTools;
    }
}
